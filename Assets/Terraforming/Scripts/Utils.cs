using Unity.Mathematics;

namespace Terraforming
{
    public static class Utils
    {
        public struct Triangle
        {
            public float3 a;
            public float3 b;
            public float3 c;

            public float3 this[int i]
            {
                get
                {
                    switch (i)
                    {
                        case 0:
                            return a;
                        case 1:
                            return b;
                        default:
                            return c;
                    }
                }
            }
        }

        public static int Flatten(int3 point, int resolution)
        {
            return point.x + point.y * resolution + point.z * resolution * resolution;
        }
    }
}