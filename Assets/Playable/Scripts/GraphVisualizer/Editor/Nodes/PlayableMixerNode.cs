using System;
using Unity.GraphToolkit.Editor;

namespace Playground.Playables.Editor
{
    [Serializable]
    internal abstract class PlayableMixerNode : AnimationPlayableNode
    {
        internal const int MinimumInputCount = 1;
        internal const int MaximumInputCount = 16;
        internal const string RuntimeKeyOptionName = "RuntimeKey";
        internal const string InputCountOptionName = "InputCount";
        internal const string InputPortPrefix = "PlayableInput";
        internal const string WeightPortPrefix = "InputWeight";

        internal int RequestedInputCount
        {
            get
            {
                var option = GetNodeOptionByName(InputCountOptionName);
                return option != null && option.TryGetValue(out int value) ? value : 2;
            }
        }

        internal string RuntimeKey
        {
            get
            {
                var option = GetNodeOptionByName(RuntimeKeyOptionName);
                return option != null && option.TryGetValue(out string value)
                    ? value?.Trim()
                    : string.Empty;
            }
        }

        internal int InputCount => UnityEngine.Mathf.Clamp(
            RequestedInputCount,
            MinimumInputCount,
            MaximumInputCount);

        internal static string GetPlayableInputPortName(int index) => $"{InputPortPrefix}{index}";
        internal static string GetWeightPortName(int index) => $"{WeightPortPrefix}{index}";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<string>(RuntimeKeyOptionName)
                .WithDisplayName("Runtime Key")
                .WithTooltip("Unique key used to query and control this mixer at runtime.")
                .WithDefaultValue(string.Empty)
                .Delayed()
                .Build();

            context.AddOption<int>(InputCountOptionName)
                .WithDisplayName("Input Count")
                .WithTooltip($"Number of playable inputs ({MinimumInputCount}-{MaximumInputCount}).")
                .WithDefaultValue(2)
                .Delayed()
                .Build();
        }

        protected void AddMixerPorts(IPortDefinitionContext context)
        {
            for (var i = 0; i < InputCount; i++)
            {
                context.AddInputPort(GetPlayableInputPortName(i))
                    .WithDisplayName($"Input {i}")
                    .WithConnectorUI(PortConnectorUI.Circle)
                    .Build();

                context.AddInputPort<float>(GetWeightPortName(i))
                    .WithDisplayName($"Weight {i}")
                    .WithDefaultValue(1f)
                    .Build();
            }

            AddPlayableOutput(context);
        }
    }
}
