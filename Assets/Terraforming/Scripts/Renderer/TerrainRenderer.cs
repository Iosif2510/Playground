using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Terraforming
{
    public class TerrainRenderer : MonoBehaviour
    {
        [SerializeField] private ChunkedDensityField densityField;
        [SerializeField, Range(-5f, 5f)] private float isoLevel = 0f;
        [SerializeField] private Material terrainMaterial; // 개별 청크에 적용할 Material
        [Header("Collider Baking")]
        [SerializeField] private bool updateColliders = true;
        [SerializeField] private bool bakeCollidersAsync = true;
        [SerializeField] private bool colliderConvex;
        [SerializeField] private bool interpolate = true;

        private Camera mainCamera;

        private NativeArray<int> edgeTableNative;
        private NativeArray<int> triTableNative;
        
        private readonly Plane[] frustumPlanes = new Plane[6];

        // 청크별 고유 렌더러 오브젝트들을 관리
        private class ChunkView 
        {
            public GameObject go;
            public MeshFilter filter;
            public MeshRenderer renderer;
            public MeshCollider collider;
            public Mesh mesh;
            public Mesh colliderMesh;
            public int colliderBakeVersion;
        }

        private struct PendingColliderBake
        {
            public ChunkView View;
            public Mesh Mesh;
            public JobHandle Handle;
            public int Version;
        }

        private readonly Dictionary<FieldChunk, ChunkView> chunkViews = new();

        // GC Alloc 방지를 위한 재사용 캐시 컬렉션
        private readonly List<FieldChunk> activeChunks = new();
        private readonly List<Vector3> verticesCache = new();
        private readonly List<int> indicesCache = new();
        private readonly List<int> edgeVertexRemapCache = new();
        private readonly List<PendingColliderBake> pendingColliderBakes = new();
        
        private bool isInitialized = false;

        private void OnEnable()
        {
            if (!isInitialized) return;
            if (densityField != null)
            {
                densityField.OnFieldUpdated += RegenerateAllMeshes;
                densityField.OnChunksUpdated += RegeneratePartialMeshes;
            }
            
            InitializeTables();

            if (densityField != null)
            {
                densityField.InitializeField();
            }
        }

        private void OnDisable()
        {
            if (!isInitialized) return;
            CompletePendingColliderBakes(assignCompletedMeshes: false);

            if (densityField != null)
            {
                densityField.OnFieldUpdated -= RegenerateAllMeshes;
                densityField.OnChunksUpdated -= RegeneratePartialMeshes;
            }

            if (edgeTableNative.IsCreated) edgeTableNative.Dispose();
            if (triTableNative.IsCreated) triTableNative.Dispose();

            foreach (var view in chunkViews.Values)
            {
                if (view.colliderMesh != null) Destroy(view.colliderMesh);
                if (view.mesh != null) Destroy(view.mesh);
                if (view.go != null) Destroy(view.go);
            }
            chunkViews.Clear();
        }

        public void Initialize()
        {
            if (densityField != null)
            {
                densityField.OnFieldUpdated += RegenerateAllMeshes;
                densityField.OnChunksUpdated += RegeneratePartialMeshes;
            }
            
            InitializeTables();
            isInitialized = true;
            mainCamera = Camera.main;
        }

        private void Update()
        {
            CompleteReadyColliderBakes();
            // if (frustumCull) FrustumCull();
        }

        private void InitializeTables()
        {
            edgeTableNative = new NativeArray<int>(LookupTable.edgeTable.Length, Allocator.Persistent);
            for (int i = 0; i < LookupTable.edgeTable.Length; i++) edgeTableNative[i] = LookupTable.edgeTable[i];

            triTableNative = new NativeArray<int>(256 * 16, Allocator.Persistent);
            for (int i = 0; i < 256; i++)
            {
                for (int j = 0; j < 16; j++)
                {
                    triTableNative[i * 16 + j] = LookupTable.triangleTable[i, j];
                }
            }
        }

        private ChunkView GetOrCreateView(FieldChunk chunk)
        {
            if (chunkViews.TryGetValue(chunk, out var view)) return view;

            var go = new GameObject($"Chunk_{chunk.OriginIndex}");
            go.transform.SetParent(transform, false);

            view = new ChunkView
            {
                go = go,
                filter = go.AddComponent<MeshFilter>(),
                renderer = go.AddComponent<MeshRenderer>(),
                collider = go.AddComponent<MeshCollider>(),
                mesh = new Mesh { name = $"Mesh_{chunk.OriginIndex}", indexFormat = IndexFormat.UInt32 }
            };

            view.mesh.MarkDynamic();
            view.filter.sharedMesh = view.mesh;
            if (terrainMaterial != null) view.renderer.sharedMaterial = terrainMaterial;

            chunkViews[chunk] = view;
            return view;
        }

        [Sirenix.OdinInspector.Button]
        public void RegenerateAllMeshes()
        {
            if (densityField == null || !densityField.Field.IsCreated || densityField.Chunks == null) return;
            ProcessChunks(densityField.Chunks);
        }

        public void RegeneratePartialMeshes(List<FieldChunk> chunksToUpdate)
        {
            if (densityField == null || !densityField.Field.IsCreated || chunksToUpdate == null) return;
            ProcessChunks(chunksToUpdate);
        }

        private void ProcessChunks(IReadOnlyList<FieldChunk> chunksToRender)
        {
            activeChunks.Clear();
            int totalIterations = 0;

            foreach (var chunk in chunksToRender)
            {
                if (!chunk.RenderMesh) continue;
                
                var size = chunk.ActualSize;
                int step = math.max(1, chunk.LodStep);

                int maxReadX = (chunk.OriginIndex.x + size.x < densityField.Resolution) ? size.x : size.x - 1;
                int maxReadY = (chunk.OriginIndex.y + size.y < densityField.Resolution) ? size.y : size.y - 1;
                int maxReadZ = (chunk.OriginIndex.z + size.z < densityField.Resolution) ? size.z : size.z - 1;

                int cellsX = maxReadX / step;
                int cellsY = maxReadY / step;
                int cellsZ = maxReadZ / step;

                totalIterations += (cellsX * cellsY * cellsZ);
                activeChunks.Add(chunk);
            }

            if (totalIterations == 0) return;

            var outputQueues = new NativeArray<NativeQueue<IndexedTriangle>>(activeChunks.Count, Allocator.TempJob);
            var edgeVertexBuffers = new List<NativeArray<float3>>(activeChunks.Count);
            var edgeCounts = new List<int3>(activeChunks.Count);
            
            for (int i = 0; i < activeChunks.Count; i++)
            {
                outputQueues[i] = new NativeQueue<IndexedTriangle>(Allocator.TempJob);
            }

            var resolution = densityField.Resolution;
            var unitSize = densityField.UnitSize;

            var jobHandles = new NativeList<JobHandle>(activeChunks.Count, Allocator.Temp);

            for (int i = 0; i < activeChunks.Count; i++)
            {
                var chunk = activeChunks[i];
                var size = chunk.ActualSize;
                int step = math.max(1, chunk.LodStep);

                int maxReadX = (chunk.OriginIndex.x + size.x < resolution) ? size.x : size.x - 1;
                int maxReadY = (chunk.OriginIndex.y + size.y < resolution) ? size.y : size.y - 1;
                int maxReadZ = (chunk.OriginIndex.z + size.z < resolution) ? size.z : size.z - 1;

                int cellsX = maxReadX / step;
                int cellsY = maxReadY / step;
                int cellsZ = maxReadZ / step;

                int iterations = cellsX * cellsY * cellsZ;

                var pointCounts = new int3(cellsX + 1, cellsY + 1, cellsZ + 1);
                int xEdgeCount = cellsX * pointCounts.y * pointCounts.z;
                int yEdgeCount = pointCounts.x * cellsY * pointCounts.z;
                int zEdgeCount = pointCounts.x * pointCounts.y * cellsZ;
                int edgeCount = xEdgeCount + yEdgeCount + zEdgeCount;

                var edgeVertices = new NativeArray<float3>(edgeCount, Allocator.TempJob);
                edgeVertexBuffers.Add(edgeVertices);
                edgeCounts.Add(new int3(xEdgeCount, yEdgeCount, zEdgeCount));

                var edgeJob = new MarchingCubeEdgeVertexJob
                {
                    Field = densityField.Field,
                    Resolution = resolution,
                    UnitSize = unitSize,
                    IsoLevel = isoLevel,
                    ChunkOriginIdx = chunk.OriginIndex,
                    WorldOrigin = chunk.WorldOrigin,
                    LodStep = step,
                    CellsPerAxis = new int3(cellsX, cellsY, cellsZ),
                    PointCounts = pointCounts,
                    XEdgeCount = xEdgeCount,
                    YEdgeCount = yEdgeCount,
                    EdgeVertices = edgeVertices,
                    Interpolate = interpolate
                };

                var edgeHandle = edgeJob.Schedule(edgeCount, 64);

                var job = new MarchingCubeChunkJob
                {
                    Field = densityField.Field,
                    EdgeTable = edgeTableNative,
                    TriTable = triTableNative,
                    Resolution = resolution,
                    UnitSize = unitSize,
                    IsoLevel = isoLevel,
                    ChunkOriginIdx = chunk.OriginIndex,
                    WorldOrigin = chunk.WorldOrigin,
                    LodStep = step,
                    CellsPerAxis = new int3(cellsX, cellsY, cellsZ),
                    MaxReadBounds = new int3(maxReadX, maxReadY, maxReadZ),
                    PointCounts = pointCounts,
                    XEdgeCount = xEdgeCount,
                    YEdgeCount = yEdgeCount,
                    OutputQueue = outputQueues[i].AsParallelWriter()
                };

                jobHandles.Add(job.Schedule(iterations, 32, edgeHandle));
            }

            JobHandle.CompleteAll(jobHandles.AsArray());
            jobHandles.Dispose();

            for (int i = 0; i < activeChunks.Count; i++)
            {
                var chunk = activeChunks[i];
                var queue = outputQueues[i];
                var edgeVertices = edgeVertexBuffers[i];
                var view = GetOrCreateView(chunk);

                int triangleCount = queue.Count;
                int indexCount = triangleCount * 3;

                if (indexCount == 0)
                {
                    view.mesh.Clear();
                    ClearCollider(view);
                    queue.Dispose();
                    edgeVertices.Dispose();
                    continue;
                }

                verticesCache.Clear();
                verticesCache.Capacity = math.max(verticesCache.Capacity, math.csum(edgeCounts[i]));
                indicesCache.Clear();
                indicesCache.Capacity = math.max(indicesCache.Capacity, indexCount);
                ResetEdgeVertexRemap(edgeVertices.Length);

                while (queue.TryDequeue(out IndexedTriangle tri))
                {
                    indicesCache.Add(GetOrCreateVertexIndex(tri.A, edgeVertices));
                    indicesCache.Add(GetOrCreateVertexIndex(tri.B, edgeVertices));
                    indicesCache.Add(GetOrCreateVertexIndex(tri.C, edgeVertices));
                }

                queue.Dispose();
                edgeVertices.Dispose();

                view.mesh.Clear();
                view.mesh.SetVertices(verticesCache);
                view.mesh.SetTriangles(indicesCache, 0);
                view.mesh.RecalculateNormals();
                view.mesh.RecalculateBounds();

                RequestColliderBake(view);
            }

            outputQueues.Dispose();
        }

        private void ResetEdgeVertexRemap(int edgeCount)
        {
            edgeVertexRemapCache.Clear();
            edgeVertexRemapCache.Capacity = math.max(edgeVertexRemapCache.Capacity, edgeCount);
            for (int i = 0; i < edgeCount; i++)
            {
                edgeVertexRemapCache.Add(-1);
            }
        }

        private int GetOrCreateVertexIndex(int edgeIndex, NativeArray<float3> edgeVertices)
        {
            int vertexIndex = edgeVertexRemapCache[edgeIndex];
            if (vertexIndex >= 0) return vertexIndex;

            vertexIndex = verticesCache.Count;
            verticesCache.Add(edgeVertices[edgeIndex]);
            edgeVertexRemapCache[edgeIndex] = vertexIndex;
            return vertexIndex;
        }

        private void RequestColliderBake(ChunkView view)
        {
            if (!updateColliders || view.collider == null) return;

            var colliderMesh = new Mesh
            {
                name = $"{view.mesh.name}_Collider",
                indexFormat = IndexFormat.UInt32
            };

            colliderMesh.SetVertices(verticesCache);
            colliderMesh.SetTriangles(indicesCache, 0);
            colliderMesh.RecalculateBounds();

            int version = ++view.colliderBakeVersion;

            if (!bakeCollidersAsync)
            {
                Physics.BakeMesh(colliderMesh.GetEntityId(), colliderConvex);
                AssignColliderMesh(view, colliderMesh, version);
                return;
            }

            var bakeJob = new BakeColliderMeshJob
            {
                MeshId = colliderMesh.GetEntityId(),
                Convex = colliderConvex
            };

            pendingColliderBakes.Add(new PendingColliderBake
            {
                View = view,
                Mesh = colliderMesh,
                Handle = bakeJob.Schedule(),
                Version = version
            });
        }

        private void CompleteReadyColliderBakes()
        {
            for (int i = pendingColliderBakes.Count - 1; i >= 0; i--)
            {
                var pendingBake = pendingColliderBakes[i];
                if (!pendingBake.Handle.IsCompleted) continue;

                pendingBake.Handle.Complete();
                AssignColliderMesh(pendingBake.View, pendingBake.Mesh, pendingBake.Version);
                pendingColliderBakes.RemoveAt(i);
            }
        }

        private void CompletePendingColliderBakes(bool assignCompletedMeshes)
        {
            for (int i = pendingColliderBakes.Count - 1; i >= 0; i--)
            {
                var pendingBake = pendingColliderBakes[i];
                pendingBake.Handle.Complete();

                if (assignCompletedMeshes)
                {
                    AssignColliderMesh(pendingBake.View, pendingBake.Mesh, pendingBake.Version);
                }
                else if (pendingBake.Mesh != null)
                {
                    Destroy(pendingBake.Mesh);
                }

                pendingColliderBakes.RemoveAt(i);
            }
        }

        private void AssignColliderMesh(ChunkView view, Mesh colliderMesh, int version)
        {
            if (view == null || colliderMesh == null)
            {
                if (colliderMesh != null) Destroy(colliderMesh);
                return;
            }

            if (version != view.colliderBakeVersion)
            {
                Destroy(colliderMesh);
                return;
            }

            if (view.collider != null)
            {
                view.collider.sharedMesh = null;
                view.collider.sharedMesh = colliderMesh;
            }

            if (view.colliderMesh != null)
            {
                Destroy(view.colliderMesh);
            }

            view.colliderMesh = colliderMesh;
        }

        private void ClearCollider(ChunkView view)
        {
            if (view?.collider != null)
            {
                view.collider.sharedMesh = null;
            }

            if (view?.colliderMesh != null)
            {
                Destroy(view.colliderMesh);
                view.colliderMesh = null;
            }

            if (view != null)
            {
                view.colliderBakeVersion++;
            }
        }
    }
}
