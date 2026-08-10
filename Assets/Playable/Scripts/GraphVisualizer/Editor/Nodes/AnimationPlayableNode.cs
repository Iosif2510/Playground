using System;
using Unity.GraphToolkit.Editor;

namespace Playground.Playables.Editor
{
    [Serializable]
    internal abstract class AnimationPlayableNode : Node
    {
        internal const string OutputPortName = "PlayableOutput";

        protected static void AddPlayableOutput(IPortDefinitionContext context)
        {
            context.AddOutputPort(OutputPortName)
                .WithDisplayName("Playable")
                .WithConnectorUI(PortConnectorUI.Circle)
                .Build();
        }
    }
}
