using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    [CreateAssetMenu(menuName = "Terraforming/Simplex 3D Generator")]
    public class Simplex3DGenerator : DensityFieldGenerator
    {
        [SerializeField] private float amplitude = 1;
        [SerializeField] private float noiseScale = 0.1f;

        public override void GenerateField(half[] field, float3 position, int resolution, float unitSize)
        {
            base.GenerateField(field, position, resolution, unitSize);
        }

        public override void GenerateField(Unity.Collections.NativeArray<half> field, float3 position, int resolution, float unitSize)
        {
            base.GenerateField(field, position, resolution, unitSize);
        }

        public override half SampleDensity(float3 worldPosition, float3 fieldOrigin, int resolution, float unitSize)
        {
            return math.half(noise.snoise(worldPosition * noiseScale) * amplitude);
        }
    }
}
