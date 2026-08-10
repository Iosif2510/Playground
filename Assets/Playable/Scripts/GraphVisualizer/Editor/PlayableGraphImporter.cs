using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Playground.Playables.Editor
{
    [ScriptedImporter(2, PlayableGraphEditorGraph.AssetExtension)]
    internal sealed class PlayableGraphImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var editorGraph = GraphDatabase.LoadGraphForImporter<PlayableGraphEditorGraph>(
                context.assetPath);
            if (editorGraph == null)
            {
                Debug.LogError($"Failed to load Playable Graph asset: {context.assetPath}");
                return;
            }

            var playableNodeModels = editorGraph.GetNodes()
                .OfType<AnimationPlayableNode>()
                .ToList();
            var nodeIndices = new Dictionary<INode, int>();
            for (var i = 0; i < playableNodeModels.Count; i++)
            {
                nodeIndices.Add(playableNodeModels[i], i);
            }

            var runtimeNodes = playableNodeModels.Select(CreateRuntimeNode).ToList();
            var runtimeConnections = BuildConnections(playableNodeModels, nodeIndices);
            var runtimeOutputs = BuildOutputs(editorGraph, nodeIndices);

            var runtimeGraph = ScriptableObject.CreateInstance<RuntimePlayableGraph>();
            runtimeGraph.name = System.IO.Path.GetFileNameWithoutExtension(context.assetPath);
            runtimeGraph.SetSerializedData(runtimeNodes, runtimeConnections, runtimeOutputs);

            context.AddObjectToAsset("RuntimePlayableGraph", runtimeGraph);
            context.SetMainObject(runtimeGraph);
        }

        private static RuntimePlayableNode CreateRuntimeNode(AnimationPlayableNode node)
        {
            switch (node)
            {
                case AnimationClipPlayableNode clipNode:
                    return RuntimePlayableNode.CreateAnimationClip(
                        Resolve<AnimationClip>(clipNode, AnimationClipPlayableNode.ClipPortName),
                        Resolve<bool>(clipNode, AnimationClipPlayableNode.ApplyFootIKPortName),
                        Resolve<bool>(clipNode, AnimationClipPlayableNode.ApplyPlayableIKPortName));

                case AnimationControllerPlayableNode controllerNode:
                    return RuntimePlayableNode.CreateAnimationController(
                        Resolve<RuntimeAnimatorController>(
                            controllerNode,
                            AnimationControllerPlayableNode.ControllerPortName));

                case AnimationMixerPlayableNode mixerNode:
                    return RuntimePlayableNode.CreateAnimationMixer(
                        mixerNode.RuntimeKey,
                        mixerNode.InputCount);

                case AnimationLayerMixerPlayableNode layerMixerNode:
                {
                    var layers = new RuntimeAnimationLayer[layerMixerNode.InputCount];
                    for (var i = 0; i < layers.Length; i++)
                    {
                        layers[i] = new RuntimeAnimationLayer(
                            Resolve<AvatarMask>(
                                layerMixerNode,
                                AnimationLayerMixerPlayableNode.GetAvatarMaskPortName(i)),
                            Resolve<bool>(
                                layerMixerNode,
                                AnimationLayerMixerPlayableNode.GetAdditivePortName(i)));
                    }

                    return RuntimePlayableNode.CreateAnimationLayerMixer(
                        layerMixerNode.RuntimeKey,
                        layerMixerNode.InputCount,
                        layers);
                }

                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(node),
                        node.GetType(),
                        "Unsupported Playable editor node.");
            }
        }

        private static List<RuntimePlayableConnection> BuildConnections(
            IReadOnlyList<AnimationPlayableNode> nodeModels,
            IReadOnlyDictionary<INode, int> nodeIndices)
        {
            var connections = new List<RuntimePlayableConnection>();
            for (var destinationIndex = 0; destinationIndex < nodeModels.Count; destinationIndex++)
            {
                if (nodeModels[destinationIndex] is not PlayableMixerNode mixerNode)
                {
                    continue;
                }

                for (var inputIndex = 0; inputIndex < mixerNode.InputCount; inputIndex++)
                {
                    var inputPort = mixerNode.GetInputPortByName(
                        PlayableMixerNode.GetPlayableInputPortName(inputIndex));
                    if (!TryGetSource(inputPort, nodeIndices, out var sourceIndex, out var sourcePort))
                    {
                        continue;
                    }

                    var weight = Resolve<float>(
                        mixerNode,
                        PlayableMixerNode.GetWeightPortName(inputIndex));
                    connections.Add(new RuntimePlayableConnection(
                        sourceIndex,
                        sourcePort,
                        destinationIndex,
                        inputIndex,
                        weight));
                }
            }

            return connections;
        }

        private static List<RuntimeAnimationPlayableOutput> BuildOutputs(
            PlayableGraphEditorGraph editorGraph,
            IReadOnlyDictionary<INode, int> nodeIndices)
        {
            var outputs = new List<RuntimeAnimationPlayableOutput>();
            foreach (var outputNode in editorGraph.GetNodes().OfType<AnimationPlayableOutputNode>())
            {
                var inputPort = outputNode.GetInputPortByName(AnimationPlayableOutputNode.InputPortName);
                if (!TryGetSource(inputPort, nodeIndices, out var sourceIndex, out var sourcePort))
                {
                    continue;
                }

                outputs.Add(new RuntimeAnimationPlayableOutput(
                    Resolve<string>(outputNode, AnimationPlayableOutputNode.NamePortName),
                    sourceIndex,
                    sourcePort,
                    Resolve<float>(outputNode, AnimationPlayableOutputNode.WeightPortName)));
            }

            return outputs;
        }

        private static bool TryGetSource(
            IPort destinationPort,
            IReadOnlyDictionary<INode, int> nodeIndices,
            out int sourceNodeIndex,
            out int sourceOutputPort)
        {
            sourceNodeIndex = -1;
            sourceOutputPort = -1;

            var sourcePort = destinationPort.firstConnectedPort;
            if (sourcePort == null || !nodeIndices.TryGetValue(sourcePort.GetNode(), out sourceNodeIndex))
            {
                return false;
            }

            var sourcePorts = sourcePort.GetNode().GetOutputPorts().ToList();
            sourceOutputPort = sourcePorts.IndexOf(sourcePort);
            return sourceOutputPort >= 0;
        }

        private static T Resolve<T>(INode node, string portName)
        {
            return PlayableGraphEditorGraph.ResolvePortValue<T>(node.GetInputPortByName(portName));
        }
    }
}
