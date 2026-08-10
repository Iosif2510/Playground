using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Playground.Playables.Editor
{
    [Serializable]
    [UseWithGraph(typeof(PlayableGraphEditorGraph))]
    internal sealed class AnimationClipPlayableNode : AnimationPlayableNode
    {
        internal const string ClipPortName = "AnimationClip";
        internal const string ApplyFootIKPortName = "ApplyFootIK";
        internal const string ApplyPlayableIKPortName = "ApplyPlayableIK";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<AnimationClip>(ClipPortName)
                .WithDisplayName("Clip")
                .Build();
            context.AddInputPort<bool>(ApplyFootIKPortName)
                .WithDisplayName("Apply Foot IK")
                .WithDefaultValue(false)
                .Build();
            context.AddInputPort<bool>(ApplyPlayableIKPortName)
                .WithDisplayName("Apply Playable IK")
                .WithDefaultValue(false)
                .Build();
            AddPlayableOutput(context);
        }
    }
}
