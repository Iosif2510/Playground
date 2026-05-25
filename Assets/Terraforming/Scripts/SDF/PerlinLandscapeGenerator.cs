using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
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
            base.GenerateField(field, position, resolution, unitSize);
        }

        public override void GenerateField(NativeArray<half> field, float3 position, int resolution, float unitSize)
        {
            var job = new GenerateFieldJob
            {
                NoiseScale = noiseScale,
                Amplitude = amplitude,
                Resolution = resolution,
                UnitSize = unitSize,
                Position = position,
                Field = field
            };
            var handle = job.Schedule(resolution * resolution * resolution, 64);
            handle.Complete();
        }

        public override half SampleDensity(float3 worldPosition, float3 fieldOrigin, int resolution, float unitSize)
        {
            return math.half((worldPosition.y * 2 / resolution) - noise.cnoise(worldPosition.xz * noiseScale) * amplitude);
        }
        
        [BurstCompile]
        private struct GenerateFieldJob : IJobParallelFor
        {
            public float NoiseScale;
            public float Amplitude;
            
            public int Resolution;
            public float UnitSize;
            public float3 Position;
            public NativeArray<half> Field;
            
            private static float3 GetWorldPositionFromIndex(int index, int resolution, float unitSize, float3 origin)
            {
                var x = index % resolution;
                var y = (index / resolution) % resolution;
                var z = index / (resolution * resolution);
                var center = new float3(resolution / 2f, resolution / 2f, resolution / 2f);
                var pos = (new float3(x, y, z) - center) * unitSize + origin;
            
                return pos;
            }
            
            public void Execute(int index)
            {
                var pos = GetWorldPositionFromIndex(index, Resolution, UnitSize, Position);
                Field[index] = math.half((pos.y * 2 / Resolution) - noise.cnoise(pos.xz * NoiseScale) * Amplitude);
            }
        }
    }
}
