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
            for (var i = 0; i < resolution * resolution * resolution; i++)
            {
                var pos = SimpleDensityField.GetWorldPositionFromIndex(i, resolution, unitSize, position);
                field[i] = math.half(noise.snoise(pos * noiseScale) * amplitude);
            }
        }

        public override void GenerateField(Unity.Collections.NativeArray<half> field, float3 position, int resolution, float unitSize)
        {
            for (var i = 0; i < resolution * resolution * resolution; i++)
            {
                var pos = SimpleDensityField.GetWorldPositionFromIndex(i, resolution, unitSize, position);
                field[i] = math.half(noise.snoise(pos * noiseScale) * amplitude);
            }
        }
    }
}