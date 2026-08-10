using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Playground.Playables.Editor
{
    [Serializable]
    [UseWithGraph(typeof(PlayableGraphEditorGraph))]
    internal sealed class AnimationControllerPlayableNode : AnimationPlayableNode
    {
        internal const string ControllerPortName = "AnimatorController";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<RuntimeAnimatorController>(ControllerPortName)
                .WithDisplayName("Controller")
                .Build();
            AddPlayableOutput(context);
        }
    }
}
