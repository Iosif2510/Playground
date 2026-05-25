using System;
using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    [CreateAssetMenu(menuName = "Terraforming/Sphere Generator")]
    public class SphereGenerator : DensityFieldGenerator
    {
        [SerializeField] private float radius;
        public float Radius => radius;

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
            return math.half(math.length(worldPosition - fieldOrigin) - radius);
        }
    }
}
