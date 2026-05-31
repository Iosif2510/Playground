using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Terraforming
{
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