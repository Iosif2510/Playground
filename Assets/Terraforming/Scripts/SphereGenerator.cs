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
        
        public override void GenerateField(float[] field, float3 position, int resolution, float unitSize)
        {
            for (var i = 0; i < resolution * resolution * resolution; i++)
            {
                var pos = SimpleDensityField.GetWorldPositionFromIndex(i, resolution, unitSize, position);
                field[i] = math.length(pos - position) - radius;
            }
        }
    }
}