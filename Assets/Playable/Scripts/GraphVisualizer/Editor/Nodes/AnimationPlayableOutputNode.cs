using System;
using Unity.GraphToolkit.Editor;

namespace Playground.Playables.Editor
{
    [Serializable]
    [UseWithGraph(typeof(PlayableGraphEditorGraph))]
    internal sealed class AnimationPlayableOutputNode : Node
    {
        internal const string InputPortName = "PlayableInput";
        internal const string NamePortName = "OutputName";
        internal const string WeightPortName = "OutputWeight";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort(InputPortName)
                .WithDisplayName("Playable")
                .WithConnectorUI(PortConnectorUI.Circle)
                .Build();
            context.AddInputPort<string>(NamePortName)
                .WithDisplayName("Output Name")
                .WithDefaultValue("Animation")
                .Build();
            context.AddInputPort<float>(WeightPortName)
                .WithDisplayName("Weight")
                .WithDefaultValue(1f)
                .Build();
        }
    }
}
