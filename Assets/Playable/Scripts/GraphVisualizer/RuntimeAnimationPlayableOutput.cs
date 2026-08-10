using System;
using UnityEngine;

namespace Playground.Playables
{
    [Serializable]
    public struct RuntimeAnimationPlayableOutput
    {
        [SerializeField] private string name;
        [SerializeField] private int sourceNodeIndex;
        [SerializeField] private int sourceOutputPort;
        [SerializeField] private float weight;

        public string Name => name;
        public int SourceNodeIndex => sourceNodeIndex;
        public int SourceOutputPort => sourceOutputPort;
        public float Weight => weight;

        public RuntimeAnimationPlayableOutput(
            string name,
            int sourceNodeIndex,
            int sourceOutputPort,
            float weight)
        {
            this.name = name;
            this.sourceNodeIndex = sourceNodeIndex;
            this.sourceOutputPort = sourceOutputPort;
            this.weight = weight;
        }
    }
}
