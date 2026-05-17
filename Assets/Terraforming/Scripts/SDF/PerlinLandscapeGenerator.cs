using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Terraforming
{
    [CreateAssetMenu(menuName = "Terraforming/Perlin Landscape Generator")]
    public class PerlinLandscapeGenerator : DensityFieldGenerator
    {
        [FormerlySerializedAs("scaleMultiplier")] [SerializeField] private float amplitude = 1;
        [SerializeField] private float noiseScale = 0.1f;
        
        public override void GenerateField(half[] field, float3 position, int resolution, float unitSize)
        {
            for (var i = 0; i < resolution * resolution * resolution; i++)
            {
                var pos = SimpleDensityField.GetWorldPositionFromIndex(i, resolution, unitSize, position);
                
                // noiseScale을 곱하여 정수 좌표 밖(소수점)을 샘플링하도록 수정
                field[i] = math.half((pos.y * 2 / resolution) - noise.cnoise(pos.xz * noiseScale) * amplitude);
            }
        }

        public override void GenerateField(Unity.Collections.NativeArray<half> field, float3 position, int resolution, float unitSize)
        {
            for (var i = 0; i < resolution * resolution * resolution; i++)
            {
                var pos = SimpleDensityField.GetWorldPositionFromIndex(i, resolution, unitSize, position);
                field[i] = math.half((pos.y * 2 / resolution) - noise.cnoise(pos.xz * noiseScale) * amplitude);
            }
        }
    }
}