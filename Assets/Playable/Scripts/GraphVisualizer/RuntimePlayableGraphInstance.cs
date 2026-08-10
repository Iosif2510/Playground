using System;
using System.Collections.Generic;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playground.Playables
{
    /// <summary>Owns a generated native PlayableGraph.</summary>
    public sealed class RuntimePlayableGraphInstance : IDisposable
    {
        private PlayableGraph graph;
        private Playable[] playables;
        private readonly Dictionary<string, int> mixerNodeIndices;
        private readonly Dictionary<string, RuntimePlayableNodeType> mixerNodeTypes;

        internal RuntimePlayableGraphInstance(
            PlayableGraph graph,
            IReadOnlyList<RuntimePlayableNode> nodes,
            Playable[] playables)
        {
            this.graph = graph;
            this.playables = playables ?? Array.Empty<Playable>();
            mixerNodeIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            mixerNodeTypes = new Dictionary<string, RuntimePlayableNodeType>(StringComparer.Ordinal);

            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node.Type != RuntimePlayableNodeType.AnimationMixer &&
                    node.Type != RuntimePlayableNodeType.AnimationLayerMixer)
                {
                    continue;
                }

                var runtimeKey = node.RuntimeKey?.Trim();
                if (string.IsNullOrWhiteSpace(runtimeKey))
                {
                    continue;
                }

                if (!mixerNodeIndices.TryAdd(runtimeKey, i))
                {
                    throw new InvalidOperationException(
                        $"Runtime Key '{runtimeKey}' is assigned to more than one mixer.");
                }

                mixerNodeTypes.Add(runtimeKey, node.Type);
            }
        }

        public PlayableGraph Graph => graph;
        public bool IsValid => graph.IsValid();
        public IEnumerable<string> MixerKeys => mixerNodeIndices.Keys;

        public bool TryGetAnimationMixer(string runtimeKey, out AnimationMixerPlayable mixer)
        {
            mixer = default;
            if (!TryGetMixerPlayable(
                    runtimeKey,
                    RuntimePlayableNodeType.AnimationMixer,
                    out var playable))
            {
                return false;
            }

            mixer = (AnimationMixerPlayable)playable;
            return mixer.IsValid();
        }

        public bool TryGetAnimationLayerMixer(
            string runtimeKey,
            out AnimationLayerMixerPlayable mixer)
        {
            mixer = default;
            if (!TryGetMixerPlayable(
                    runtimeKey,
                    RuntimePlayableNodeType.AnimationLayerMixer,
                    out var playable))
            {
                return false;
            }

            mixer = (AnimationLayerMixerPlayable)playable;
            return mixer.IsValid();
        }

        public bool TrySetInputWeight(string runtimeKey, int inputIndex, float weight)
        {
            if (!TryGetMixerPlayable(runtimeKey, out var playable) ||
                inputIndex < 0 ||
                inputIndex >= playable.GetInputCount())
            {
                return false;
            }

            playable.SetInputWeight(inputIndex, weight);
            return true;
        }

        public bool TryGetInputWeight(string runtimeKey, int inputIndex, out float weight)
        {
            weight = default;
            if (!TryGetMixerPlayable(runtimeKey, out var playable) ||
                inputIndex < 0 ||
                inputIndex >= playable.GetInputCount())
            {
                return false;
            }

            weight = playable.GetInputWeight(inputIndex);
            return true;
        }

        public void Play()
        {
            if (graph.IsValid())
            {
                graph.Play();
            }
        }

        public void Stop()
        {
            if (graph.IsValid())
            {
                graph.Stop();
            }
        }

        public void Evaluate(float deltaTime = 0f)
        {
            if (graph.IsValid())
            {
                graph.Evaluate(deltaTime);
            }
        }

        public void Dispose()
        {
            if (graph.IsValid())
            {
                graph.Destroy();
            }

            playables = Array.Empty<Playable>();
            mixerNodeIndices.Clear();
            mixerNodeTypes.Clear();
        }

        private bool TryGetMixerPlayable(string runtimeKey, out Playable playable)
        {
            playable = default;
            if (!graph.IsValid() ||
                string.IsNullOrWhiteSpace(runtimeKey) ||
                !mixerNodeIndices.TryGetValue(runtimeKey.Trim(), out var nodeIndex) ||
                nodeIndex < 0 ||
                nodeIndex >= playables.Length)
            {
                return false;
            }

            playable = playables[nodeIndex];
            return playable.IsValid();
        }

        private bool TryGetMixerPlayable(
            string runtimeKey,
            RuntimePlayableNodeType expectedType,
            out Playable playable)
        {
            playable = default;
            if (!TryGetMixerPlayable(runtimeKey, out var candidate) ||
                !mixerNodeTypes.TryGetValue(runtimeKey.Trim(), out var actualType) ||
                actualType != expectedType)
            {
                return false;
            }

            playable = candidate;
            return true;
        }
    }
}
