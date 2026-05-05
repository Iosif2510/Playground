using System;
using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    [Serializable]
    public abstract class DensityFieldGenerator : ScriptableObject
    {
        public abstract void GenerateField(float[] field, float3 position, int resolution, float unitSize);
    }
}