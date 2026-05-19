using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    [Serializable, BurstCompile]
    public struct PerlinOctave
    {
        public float Amplitude;
        public float NoiseScale;
    }
    
    [CreateAssetMenu(menuName = "Terraforming/Multiple Perlin Generator")]
    public class MultiplePerlinGenerator : DensityFieldGenerator
    {
        [SerializeField] private List<PerlinOctave> octaves;
        
        public override void GenerateField(half[] field, float3 position, int resolution, float unitSize)
        {
            throw new System.NotImplementedException();
        }

        public override void GenerateField(NativeArray<half> field, float3 position, int resolution, float unitSize)
        {
            var count = resolution * resolution * resolution;

            var octaveArray = octaves.ToNativeArray(Allocator.TempJob);
            var job = new GenerateFieldJob
            {
                Octaves = octaveArray,
                Resolution = resolution,
                UnitSize = unitSize,
                Position = position,
                Field = field
            };
            var handle = job.Schedule(count, 64);
            handle.Complete();
            octaveArray.Dispose();
        }
        
        [BurstCompile]
        private struct ResetFieldJob : IJobParallelFor
        {
            public NativeArray<half> Field;
            
            public void Execute(int index)
            {
                Field[index] = math.half(0f);
            }
        }
        
        [BurstCompile]
        private struct GenerateFieldJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<PerlinOctave> Octaves;
            
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
                float noiseHeight = 0f;
                
                for (int i = 0; i < Octaves.Length; i++)
                {
                    noiseHeight += noise.cnoise(pos.xz * Octaves[i].NoiseScale) * Octaves[i].Amplitude;
                }
                
                Field[index] = math.half((pos.y * 2 / Resolution) - noiseHeight);
            }
        }
        
    }
    

}