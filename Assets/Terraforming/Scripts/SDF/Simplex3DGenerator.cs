using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    [CreateAssetMenu(menuName = "Terraforming/Simplex 3D Generator")]
    public class Simplex3DGenerator : DensityFieldGenerator
    {
        [SerializeField] private float amplitude = 1;
        [SerializeField] private float noiseScale = 0.1f;
        
        public override void GenerateField(float[] field, float3 position, int resolution, float unitSize)
        {
            for (var i = 0; i < resolution * resolution * resolution; i++)
            {
                var pos = SimpleDensityField.GetWorldPositionFromIndex(i, resolution, unitSize, position);
                field[i] = noise.snoise(pos * noiseScale) * amplitude;
            }
        }
    }
}