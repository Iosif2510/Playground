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
        }

        private readonly Dictionary<FieldChunk, ChunkView> chunkViews = new();

        // GC Alloc 방지를 위한 재사용 캐시 컬렉션
        private readonly List<FieldChunk> activeChunks = new();
        private readonly List<Vector3> verticesCache = new();
        private readonly List<int> indicesCache = new();

        private void OnEnable()
        {
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
            if (densityField != null)
            {
                densityField.OnFieldUpdated -= RegenerateAllMeshes;
                densityField.OnChunksUpdated -= RegeneratePartialMeshes;
            }

            if (edgeTableNative.IsCreated) edgeTableNative.Dispose();
            if (triTableNative.IsCreated) triTableNative.Dispose();

            foreach (var view in chunkViews.Values)
            {
                if (view.mesh != null) Destroy(view.mesh);
                if (view.go != null) Destroy(view.go);
            }
            chunkViews.Clear();
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
            view.collider.sharedMesh = view.mesh;
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

            var outputQueues = new NativeArray<NativeQueue<Triangle>>(activeChunks.Count, Allocator.TempJob);
            
            for (int i = 0; i < activeChunks.Count; i++)
            {
                outputQueues[i] = new NativeQueue<Triangle>(Allocator.TempJob);
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
                    OutputQueue = outputQueues[i].AsParallelWriter()
                };

                jobHandles.Add(job.Schedule(iterations, 32));
            }

            JobHandle.CompleteAll(jobHandles.AsArray());
            jobHandles.Dispose();

            // C# Dictionary를 이용한 메인 스레드 고속 버텍스 웰딩(Welding) 캐시
            var sharedVertices = new Dictionary<float3, int>();

            for (int i = 0; i < activeChunks.Count; i++)
            {
                var chunk = activeChunks[i];
                var queue = outputQueues[i];
                var view = GetOrCreateView(chunk);

                int triangleCount = queue.Count;
                int vertexCount = triangleCount * 3;

                if (vertexCount == 0)
                {
                    view.mesh.Clear();
                    queue.Dispose();
                    continue;
                }

                verticesCache.Clear();
                verticesCache.Capacity = math.max(verticesCache.Capacity, vertexCount);
                indicesCache.Clear();
                indicesCache.Capacity = math.max(indicesCache.Capacity, vertexCount);
                sharedVertices.Clear();

                while (queue.TryDequeue(out Triangle tri))
                {
                    // A
                    if (!sharedVertices.TryGetValue(tri.A, out int idxA))
                    {
                        idxA = verticesCache.Count;
                        verticesCache.Add(tri.A);
                        sharedVertices[tri.A] = idxA;
                    }
                    indicesCache.Add(idxA);

                    // B
                    if (!sharedVertices.TryGetValue(tri.B, out int idxB))
                    {
                        idxB = verticesCache.Count;
                        verticesCache.Add(tri.B);
                        sharedVertices[tri.B] = idxB;
                    }
                    indicesCache.Add(idxB);

                    // C
                    if (!sharedVertices.TryGetValue(tri.C, out int idxC))
                    {
                        idxC = verticesCache.Count;
                        verticesCache.Add(tri.C);
                        sharedVertices[tri.C] = idxC;
                    }
                    indicesCache.Add(idxC);
                }

                queue.Dispose();

                view.mesh.Clear();
                view.mesh.SetVertices(verticesCache);
                view.mesh.SetTriangles(indicesCache, 0);
                view.mesh.RecalculateNormals();
                view.mesh.RecalculateBounds();

                // MeshCollider는 매번 null 처리를 한 뒤 다시 덮어주어야 갱신(Bake)이 일어납니다.
                view.collider.sharedMesh = null;
                view.collider.sharedMesh = view.mesh;
            }

            outputQueues.Dispose();
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

        public NativeQueue<Triangle>.ParallelWriter OutputQueue;

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
            int ix = index % CellsPerAxis.x;
            int iy = (index / CellsPerAxis.x) % CellsPerAxis.y;
            int iz = index / (CellsPerAxis.x * CellsPerAxis.y);

            int x = ix * LodStep;
            int y = iy * LodStep;
            int z = iz * LodStep;

            if (x + LodStep > MaxReadBounds.x || y + LodStep > MaxReadBounds.y || z + LodStep > MaxReadBounds.z) return;

            var cubeValues = new NativeArray<float>(8, Allocator.Temp);
            var cubePoints = new NativeArray<float3>(8, Allocator.Temp);

            cubeValues[0] = Field[FlattenGlobal(x, y, z)];
            cubeValues[1] = Field[FlattenGlobal(x + LodStep, y, z)];
            cubeValues[2] = Field[FlattenGlobal(x + LodStep, y, z + LodStep)];
            cubeValues[3] = Field[FlattenGlobal(x, y, z + LodStep)];
            cubeValues[4] = Field[FlattenGlobal(x, y + LodStep, z)];
            cubeValues[5] = Field[FlattenGlobal(x + LodStep, y + LodStep, z)];
            cubeValues[6] = Field[FlattenGlobal(x + LodStep, y + LodStep, z + LodStep)];
            cubeValues[7] = Field[FlattenGlobal(x, y + LodStep, z + LodStep)];

            cubePoints[0] = GetWorldPosition(x, y, z);
            cubePoints[1] = GetWorldPosition(x + LodStep, y, z);
            cubePoints[2] = GetWorldPosition(x + LodStep, y, z + LodStep);
            cubePoints[3] = GetWorldPosition(x, y, z + LodStep);
            cubePoints[4] = GetWorldPosition(x, y + LodStep, z);
            cubePoints[5] = GetWorldPosition(x + LodStep, y + LodStep, z);
            cubePoints[6] = GetWorldPosition(x + LodStep, y + LodStep, z + LodStep);
            cubePoints[7] = GetWorldPosition(x, y + LodStep, z + LodStep);

            int cubeIndex = 0;
            for (var i = 0; i < 8; i++)
            {
                if (cubeValues[i] < IsoLevel) cubeIndex |= (1 << i);
            }

            var edgeTableValue = EdgeTable[cubeIndex];
            if (edgeTableValue == 0) return;

            var vertList = new NativeArray<float3>(12, Allocator.Temp);

            // Edge Pairs
            var edgePairs = new NativeArray<int2>(12, Allocator.Temp);
            edgePairs[0] = new int2(0, 1); edgePairs[1] = new int2(1, 2); edgePairs[2] = new int2(2, 3); edgePairs[3] = new int2(3, 0);
            edgePairs[4] = new int2(4, 5); edgePairs[5] = new int2(5, 6); edgePairs[6] = new int2(6, 7); edgePairs[7] = new int2(7, 4);
            edgePairs[8] = new int2(0, 4); edgePairs[9] = new int2(1, 5); edgePairs[10] = new int2(2, 6); edgePairs[11] = new int2(3, 7);

            for (var i = 0; i < 12; i++)
            {
                if ((edgeTableValue & (1 << i)) != 0)
                {
                    int p1 = edgePairs[i].x;
                    int p2 = edgePairs[i].y;
                    
                    float t = (IsoLevel - cubeValues[p1]) / (cubeValues[p2] - cubeValues[p1]);
                    vertList[i] = cubePoints[p1] + t * (cubePoints[p2] - cubePoints[p1]);
                }
            }

            for (int i = 0; i < 16; i += 3)
            {
                int triIndex = TriTable[cubeIndex * 16 + i];
                if (triIndex == -1) break;

                OutputQueue.Enqueue(new Triangle
                {
                    A = vertList[triIndex],
                    B = vertList[TriTable[cubeIndex * 16 + i + 1]],
                    C = vertList[TriTable[cubeIndex * 16 + i + 2]]
                });
            }
        }
    }
}