using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Playground.Playables.Editor
{
    [Serializable]
    [UseWithGraph(typeof(PlayableGraphEditorGraph))]
    internal sealed class AnimationLayerMixerPlayableNode : PlayableMixerNode
    {
        internal const string AvatarMaskPortPrefix = "AvatarMask";
        internal const string AdditivePortPrefix = "IsAdditive";

        internal static string GetAvatarMaskPortName(int index) => $"{AvatarMaskPortPrefix}{index}";
        internal static string GetAdditivePortName(int index) => $"{AdditivePortPrefix}{index}";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            for (var i = 0; i < InputCount; i++)
            {
                context.AddInputPort(GetPlayableInputPortName(i))
                    .WithDisplayName($"Layer {i}")
                    .WithConnectorUI(PortConnectorUI.Circle)
                    .Build();
                context.AddInputPort<float>(GetWeightPortName(i))
                    .WithDisplayName($"Weight {i}")
                    .WithDefaultValue(1f)
                    .Build();
                context.AddInputPort<AvatarMask>(GetAvatarMaskPortName(i))
                    .WithDisplayName($"Mask {i}")
                    .Build();
                context.AddInputPort<bool>(GetAdditivePortName(i))
                    .WithDisplayName($"Additive {i}")
                    .WithDefaultValue(false)
                    .Build();
            }

            AddPlayableOutput(context);
        }
    }
}
