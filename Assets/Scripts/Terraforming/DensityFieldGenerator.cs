using System;
using Unity.Mathematics;

namespace Terraforming
{
    [Serializable]
    public abstract class DensityFieldGenerator
    {
        public abstract void GenerateField(FieldData[] field, float3 position, int resolution, float unitSize);
    }
}