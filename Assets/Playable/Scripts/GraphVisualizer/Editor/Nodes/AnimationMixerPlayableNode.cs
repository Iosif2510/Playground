using System;
using Unity.GraphToolkit.Editor;

namespace Playground.Playables.Editor
{
    [Serializable]
    [UseWithGraph(typeof(PlayableGraphEditorGraph))]
    internal sealed class AnimationMixerPlayableNode : PlayableMixerNode
    {
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            AddMixerPorts(context);
        }
    }
}
