using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playground.Playables
{
    /// <summary>
    /// The runtime representation generated from a Playable Graph Toolkit asset.
    /// </summary>
    public sealed class RuntimePlayableGraph : ScriptableObject
    {
        [SerializeField] private List<RuntimePlayableNode> nodes = new();
        [SerializeField] private List<RuntimePlayableConnection> connections = new();
        [SerializeField] private List<RuntimeAnimationPlayableOutput> outputs = new();

        public IReadOnlyList<RuntimePlayableNode> Nodes => nodes;
        public IReadOnlyList<RuntimePlayableConnection> Connections => connections;
        public IReadOnlyList<RuntimeAnimationPlayableOutput> Outputs => outputs;

        /// <summary>
        /// Creates the Unity PlayableGraph represented by this asset. The returned instance owns
        /// the native graph and must be disposed when it is no longer needed.
        /// </summary>
        public RuntimePlayableGraphInstance CreateInstance(
            Animator targetAnimator,
            DirectorUpdateMode updateMode = DirectorUpdateMode.GameTime)
        {
            if (targetAnimator == null && outputs.Count > 0)
            {
                throw new ArgumentNullException(
                    nameof(targetAnimator),
                    "An Animator is required when the graph contains an AnimationPlayableOutput.");
            }

            var graph = UnityEngine.Playables.PlayableGraph.Create(name);
            graph.SetTimeUpdateMode(updateMode);

            try
            {
                var playables = new Playable[nodes.Count];
                for (var i = 0; i < nodes.Count; i++)
                {
                    playables[i] = nodes[i].CreatePlayable(graph, i);
                }

                foreach (var connection in connections)
                {
                    ValidateNodeIndex(connection.SourceNodeIndex, playables.Length, "source");
                    ValidateNodeIndex(connection.DestinationNodeIndex, playables.Length, "destination");

                    var source = playables[connection.SourceNodeIndex];
                    var destination = playables[connection.DestinationNodeIndex];
                    if (connection.DestinationInputPort < 0 ||
                        connection.DestinationInputPort >= destination.GetInputCount())
                    {
                        throw new InvalidOperationException(
                            $"Input {connection.DestinationInputPort} does not exist on runtime node " +
                            $"{connection.DestinationNodeIndex}.");
                    }

                    if (!graph.Connect(
                            source,
                            connection.SourceOutputPort,
                            destination,
                            connection.DestinationInputPort))
                    {
                        throw new InvalidOperationException(
                            $"Could not connect runtime node {connection.SourceNodeIndex} to " +
                            $"node {connection.DestinationNodeIndex}, input {connection.DestinationInputPort}.");
                    }

                    destination.SetInputWeight(connection.DestinationInputPort, connection.Weight);
                }

                foreach (var outputData in outputs)
                {
                    ValidateNodeIndex(outputData.SourceNodeIndex, playables.Length, "output source");

                    var output = AnimationPlayableOutput.Create(
                        graph,
                        string.IsNullOrWhiteSpace(outputData.Name) ? "Animation" : outputData.Name,
                        targetAnimator);
                    output.SetSourcePlayable(
                        playables[outputData.SourceNodeIndex],
                        outputData.SourceOutputPort);
                    output.SetWeight(outputData.Weight);
                }

                return new RuntimePlayableGraphInstance(graph, nodes, playables);
            }
            catch
            {
                if (graph.IsValid())
                {
                    graph.Destroy();
                }

                throw;
            }
        }

        private static void ValidateNodeIndex(int index, int nodeCount, string role)
        {
            if (index < 0 || index >= nodeCount)
            {
                throw new InvalidOperationException(
                    $"The {role} node index {index} is outside the runtime graph (node count: {nodeCount}).");
            }
        }

#if UNITY_EDITOR
        /// <summary>Used by the editor importer to replace the generated runtime data.</summary>
        public void SetSerializedData(
            List<RuntimePlayableNode> runtimeNodes,
            List<RuntimePlayableConnection> runtimeConnections,
            List<RuntimeAnimationPlayableOutput> runtimeOutputs)
        {
            nodes = runtimeNodes ?? new List<RuntimePlayableNode>();
            connections = runtimeConnections ?? new List<RuntimePlayableConnection>();
            outputs = runtimeOutputs ?? new List<RuntimeAnimationPlayableOutput>();
        }
#endif
    }
}
