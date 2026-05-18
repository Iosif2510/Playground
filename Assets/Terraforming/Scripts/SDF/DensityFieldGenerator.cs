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
        public abstract void GenerateField(half[] field, float3 position, int resolution, float unitSize);
        public abstract void GenerateField(NativeArray<half> field, float3 position, int resolution, float unitSize);
    }
}