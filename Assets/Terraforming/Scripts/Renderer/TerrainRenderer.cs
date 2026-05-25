using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terraforming
{
    public struct Triangle
    {
        public float3 A;
        public float3 B;
        public float3 C;
    }

    public struct IndexedTriangle
    {
        public int A;
        public int B;
        public int C;
    }

    public class TerrainRenderer : MonoBehaviour
    {
        [SerializeField] private ChunkedDensityField densityField;
        [SerializeField, Range(-5f, 5f)] private float isoLevel = 0f;
        [SerializeField] private Material terrainMaterial; // 개별 청크에 적용할 Material
        [Header("Collider Baking")]
        [SerializeField] private bool updateColliders = true;
        [SerializeField] private bool bakeCollidersAsync = true;
        [SerializeField] private bool colliderConvex;

        private NativeArray<int> edgeTableNative;
        private NativeArray<int> triTableNative;

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
        }

        private void Update()
        {
            CompleteReadyColliderBakes();
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
                int step = math.max(1, chunk.Lod);

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
                int step = math.max(1, chunk.Lod);

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
                    EdgeVertices = edgeVertices
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

    public struct BakeColliderMeshJob : IJob
    {
        public EntityId MeshId;
        public bool Convex;

        public void Execute()
        {
            Physics.BakeMesh(MeshId, Convex);
        }
    }

    [BurstCompile]
    public struct MarchingCubeEdgeVertexJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<half> Field;

        public int Resolution;
        public float UnitSize;
        public float IsoLevel;

        public int3 ChunkOriginIdx;
        public float3 WorldOrigin;
        public int LodStep;

        public int3 CellsPerAxis;
        public int3 PointCounts;
        public int XEdgeCount;
        public int YEdgeCount;

        public NativeArray<float3> EdgeVertices;

        private int FlattenGlobal(int lx, int ly, int lz)
        {
            return (ChunkOriginIdx.x + lx) +
                   (ChunkOriginIdx.y + ly) * Resolution +
                   (ChunkOriginIdx.z + lz) * Resolution * Resolution;
        }

        private float3 GetWorldPosition(int lx, int ly, int lz)
        {
            return WorldOrigin + new float3(lx, ly, lz) * UnitSize;
        }

        public void Execute(int index)
        {
            int axis;
            int px;
            int py;
            int pz;

            if (index < XEdgeCount)
            {
                axis = 0;
                px = index % CellsPerAxis.x;
                py = (index / CellsPerAxis.x) % PointCounts.y;
                pz = index / (CellsPerAxis.x * PointCounts.y);
            }
            else if (index < XEdgeCount + YEdgeCount)
            {
                axis = 1;
                int localIndex = index - XEdgeCount;
                px = localIndex % PointCounts.x;
                py = (localIndex / PointCounts.x) % CellsPerAxis.y;
                pz = localIndex / (PointCounts.x * CellsPerAxis.y);
            }
            else
            {
                axis = 2;
                int localIndex = index - XEdgeCount - YEdgeCount;
                px = localIndex % PointCounts.x;
                py = (localIndex / PointCounts.x) % PointCounts.y;
                pz = localIndex / (PointCounts.x * PointCounts.y);
            }

            int lx0 = px * LodStep;
            int ly0 = py * LodStep;
            int lz0 = pz * LodStep;

            int lx1 = lx0 + (axis == 0 ? LodStep : 0);
            int ly1 = ly0 + (axis == 1 ? LodStep : 0);
            int lz1 = lz0 + (axis == 2 ? LodStep : 0);

            float value0 = Field[FlattenGlobal(lx0, ly0, lz0)];
            float value1 = Field[FlattenGlobal(lx1, ly1, lz1)];

            float denominator = value1 - value0;
            float t = math.abs(denominator) > 0.000001f ? (IsoLevel - value0) / denominator : 0.5f;

            EdgeVertices[index] = math.lerp(
                GetWorldPosition(lx0, ly0, lz0),
                GetWorldPosition(lx1, ly1, lz1),
                t);
        }
    }

    [BurstCompile]
    public struct MarchingCubeChunkJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<half> Field;
        [ReadOnly] public NativeArray<int> EdgeTable;
        [ReadOnly] public NativeArray<int> TriTable;
        
        public int Resolution;
        public float UnitSize;
        public float IsoLevel;

        public int3 ChunkOriginIdx;
        public float3 WorldOrigin;
        public int LodStep;

        public int3 CellsPerAxis;
        public int3 MaxReadBounds;
        public int3 PointCounts;
        public int XEdgeCount;
        public int YEdgeCount;

        public NativeQueue<IndexedTriangle>.ParallelWriter OutputQueue;

        private int FlattenGlobal(int lx, int ly, int lz)
        {
            return (ChunkOriginIdx.x + lx) + 
                   (ChunkOriginIdx.y + ly) * Resolution + 
                   (ChunkOriginIdx.z + lz) * Resolution * Resolution;
        }

        private int GetEdgeVertexIndex(int ix, int iy, int iz, int cubeEdgeIndex)
        {
            return cubeEdgeIndex switch
            {
                0 => GetXEdgeIndex(ix, iy, iz),
                1 => GetZEdgeIndex(ix + 1, iy, iz),
                2 => GetXEdgeIndex(ix, iy, iz + 1),
                3 => GetZEdgeIndex(ix, iy, iz),
                4 => GetXEdgeIndex(ix, iy + 1, iz),
                5 => GetZEdgeIndex(ix + 1, iy + 1, iz),
                6 => GetXEdgeIndex(ix, iy + 1, iz + 1),
                7 => GetZEdgeIndex(ix, iy + 1, iz),
                8 => GetYEdgeIndex(ix, iy, iz),
                9 => GetYEdgeIndex(ix + 1, iy, iz),
                10 => GetYEdgeIndex(ix + 1, iy, iz + 1),
                _ => GetYEdgeIndex(ix, iy, iz + 1)
            };
        }

        private int GetXEdgeIndex(int px, int py, int pz)
        {
            return px + py * CellsPerAxis.x + pz * CellsPerAxis.x * PointCounts.y;
        }

        private int GetYEdgeIndex(int px, int py, int pz)
        {
            return XEdgeCount + px + py * PointCounts.x + pz * PointCounts.x * CellsPerAxis.y;
        }

        private int GetZEdgeIndex(int px, int py, int pz)
        {
            return XEdgeCount + YEdgeCount + px + py * PointCounts.x + pz * PointCounts.x * PointCounts.y;
        }

        public void Execute(int index)
        {
            int ix = index % CellsPerAxis.x;
            int iy = (index / CellsPerAxis.x) % CellsPerAxis.y;
            int iz = index / (CellsPerAxis.x * CellsPerAxis.y);

            int x = ix * LodStep;
            int y = iy * LodStep;
            int z = iz * LodStep;

            if (x + LodStep > MaxReadBounds.x || y + LodStep > MaxReadBounds.y || z + LodStep > MaxReadBounds.z) return;

            var cubeValues = new FixedList128Bytes<float>();

            cubeValues.Add(Field[FlattenGlobal(x, y, z)]);
            cubeValues.Add(Field[FlattenGlobal(x + LodStep, y, z)]);
            cubeValues.Add(Field[FlattenGlobal(x + LodStep, y, z + LodStep)]);
            cubeValues.Add(Field[FlattenGlobal(x, y, z + LodStep)]);
            cubeValues.Add(Field[FlattenGlobal(x, y + LodStep, z)]);
            cubeValues.Add(Field[FlattenGlobal(x + LodStep, y + LodStep, z)]);
            cubeValues.Add(Field[FlattenGlobal(x + LodStep, y + LodStep, z + LodStep)]);
            cubeValues.Add(Field[FlattenGlobal(x, y + LodStep, z + LodStep)]);

            int cubeIndex = 0;
            for (var i = 0; i < 8; i++)
            {
                if (cubeValues[i] < IsoLevel) cubeIndex |= (1 << i);
            }

            var edgeTableValue = EdgeTable[cubeIndex];
            if (edgeTableValue == 0) return;

            for (int i = 0; i < 16; i += 3)
            {
                int triIndex = TriTable[cubeIndex * 16 + i];
                if (triIndex == -1) break;

                OutputQueue.Enqueue(new IndexedTriangle
                {
                    A = GetEdgeVertexIndex(ix, iy, iz, triIndex),
                    B = GetEdgeVertexIndex(ix, iy, iz, TriTable[cubeIndex * 16 + i + 1]),
                    C = GetEdgeVertexIndex(ix, iy, iz, TriTable[cubeIndex * 16 + i + 2])
                });
            }
        }
    }
}
