using System;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;

namespace Playground.Playables.Editor
{
    [Serializable]
    [Graph(AssetExtension, GraphOptions.DisableAutoInclusionOfNodesFromGraphAssembly)]
    internal sealed class PlayableGraphEditorGraph : Graph
    {
        internal const string AssetExtension = "playablegraph";

        [MenuItem("Assets/Create/Playable/Playable Graph")]
        private static void CreateAssetFile()
        {
            GraphDatabase.PromptInProjectBrowserToCreateNewAsset<PlayableGraphEditorGraph>(
                "New Playable Graph");
        }

        public override void OnGraphChanged(GraphLogger logger)
        {
            base.OnGraphChanged(logger);

            var graphNodes = GetNodes().ToList();
            var outputNodes = graphNodes.OfType<AnimationPlayableOutputNode>().ToList();
            if (outputNodes.Count == 0)
            {
                logger.LogError("Add at least one AnimationPlayableOutput node.", this);
            }

            foreach (var outputNode in outputNodes)
            {
                if (!outputNode.GetInputPortByName(AnimationPlayableOutputNode.InputPortName).isConnected)
                {
                    logger.LogError("AnimationPlayableOutput requires a playable input.", outputNode);
                }
            }

            foreach (var clipNode in graphNodes.OfType<AnimationClipPlayableNode>())
            {
                var clip = ResolvePortValue<UnityEngine.AnimationClip>(
                    clipNode.GetInputPortByName(AnimationClipPlayableNode.ClipPortName));
                if (clip == null)
                {
                    logger.LogError("AnimationClipPlayable requires an AnimationClip.", clipNode);
                }
            }

            foreach (var controllerNode in graphNodes.OfType<AnimationControllerPlayableNode>())
            {
                var controller = ResolvePortValue<UnityEngine.RuntimeAnimatorController>(
                    controllerNode.GetInputPortByName(AnimationControllerPlayableNode.ControllerPortName));
                if (controller == null)
                {
                    logger.LogError(
                        "AnimationControllerPlayable requires a RuntimeAnimatorController.",
                        controllerNode);
                }
            }

            foreach (var mixerNode in graphNodes.OfType<AnimationMixerPlayableNode>())
            {
                LogInputCountWarning(logger, mixerNode, mixerNode.RequestedInputCount);
            }

            foreach (var layerMixerNode in graphNodes.OfType<AnimationLayerMixerPlayableNode>())
            {
                LogInputCountWarning(logger, layerMixerNode, layerMixerNode.RequestedInputCount);
            }

            var mixerNodes = graphNodes.OfType<PlayableMixerNode>().ToList();
            foreach (var mixerNode in mixerNodes.Where(node => string.IsNullOrWhiteSpace(node.RuntimeKey)))
            {
                logger.LogWarning(
                    "Set Runtime Key to query and control this mixer at runtime.",
                    mixerNode);
            }

            foreach (var duplicateGroup in mixerNodes
                         .Where(node => !string.IsNullOrWhiteSpace(node.RuntimeKey))
                         .GroupBy(node => node.RuntimeKey, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1))
            {
                foreach (var mixerNode in duplicateGroup)
                {
                    logger.LogError(
                        $"Runtime Key '{duplicateGroup.Key}' is used by more than one mixer.",
                        mixerNode);
                }
            }
        }

        internal static T ResolvePortValue<T>(IPort port)
        {
            var sourcePort = port.firstConnectedPort;
            switch (sourcePort?.GetNode())
            {
                case IConstantNode constantNode when constantNode.TryGetValue(out T constantValue):
                    return constantValue;
                case IVariableNode variableNode when variableNode.variable.TryGetDefaultValue(out T variableValue):
                    return variableValue;
                case null when port.TryGetValue(out T embeddedValue):
                    return embeddedValue;
                default:
                    return default;
            }
        }

        private static void LogInputCountWarning(GraphLogger logger, INode node, int requestedInputCount)
        {
            if (requestedInputCount < PlayableMixerNode.MinimumInputCount ||
                requestedInputCount > PlayableMixerNode.MaximumInputCount)
            {
                logger.LogWarning(
                    $"Input Count is clamped to {PlayableMixerNode.MinimumInputCount}-" +
                    $"{PlayableMixerNode.MaximumInputCount}.",
                    node);
            }
        }
    }
}
