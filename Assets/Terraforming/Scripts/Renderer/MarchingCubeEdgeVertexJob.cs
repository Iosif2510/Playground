using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Terraforming
{
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

        public bool Interpolate;

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

            EdgeVertices[index] = Interpolate
                ? math.lerp(
                    GetWorldPosition(lx0, ly0, lz0),
                    GetWorldPosition(lx1, ly1, lz1),
                    t)
                : (GetWorldPosition(lx0, ly0, lz0) + GetWorldPosition(lx1, ly1, lz1)) / 2f;
        }
    }
}