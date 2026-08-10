using System;
using UnityEngine;

namespace Playground.Playables
{
    [Serializable]
    public struct RuntimePlayableConnection
    {
        [SerializeField] private int sourceNodeIndex;
        [SerializeField] private int sourceOutputPort;
        [SerializeField] private int destinationNodeIndex;
        [SerializeField] private int destinationInputPort;
        [SerializeField] private float weight;

        public int SourceNodeIndex => sourceNodeIndex;
        public int SourceOutputPort => sourceOutputPort;
        public int DestinationNodeIndex => destinationNodeIndex;
        public int DestinationInputPort => destinationInputPort;
        public float Weight => weight;

        public RuntimePlayableConnection(
            int sourceNodeIndex,
            int sourceOutputPort,
            int destinationNodeIndex,
            int destinationInputPort,
            float weight)
        {
            this.sourceNodeIndex = sourceNodeIndex;
            this.sourceOutputPort = sourceOutputPort;
            this.destinationNodeIndex = destinationNodeIndex;
            this.destinationInputPort = destinationInputPort;
            this.weight = weight;
        }
    }
}
