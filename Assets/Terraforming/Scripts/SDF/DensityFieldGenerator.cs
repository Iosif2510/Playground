using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    [Serializable]
    public abstract class DensityFieldGenerator : ScriptableObject
    {
        [Obsolete]
        public virtual void GenerateField(half[] field, float3 position, int resolution, float unitSize)
        {
            for (var i = 0; i < resolution * resolution * resolution; i++)
            {
                field[i] = SampleDensity(SimpleDensityField.GetWorldPositionFromIndex(i, resolution, unitSize, position), position, resolution, unitSize);
            }
        }

        public virtual void GenerateField(NativeArray<half> field, float3 position, int resolution, float unitSize)
        {
            for (var i = 0; i < resolution * resolution * resolution; i++)
            {
                field[i] = SampleDensity(SimpleDensityField.GetWorldPositionFromIndex(i, resolution, unitSize, position), position, resolution, unitSize);
            }
        }

        public abstract half SampleDensity(float3 worldPosition, float3 fieldOrigin, int resolution, float unitSize);
    }
}
