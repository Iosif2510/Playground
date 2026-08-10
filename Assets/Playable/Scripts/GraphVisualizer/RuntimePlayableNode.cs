using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Playground.Playables
{
    [Serializable]
    public sealed class RuntimePlayableNode
    {
        [SerializeField] private RuntimePlayableNodeType type;
        [SerializeField] private string runtimeKey;
        [SerializeField] private AnimationClip animationClip;
        [SerializeField] private RuntimeAnimatorController animatorController;
        [SerializeField] private bool applyFootIK;
        [SerializeField] private bool applyPlayableIK;
        [SerializeField] private int inputCount;
        [SerializeField] private RuntimeAnimationLayer[] layers = Array.Empty<RuntimeAnimationLayer>();

        public RuntimePlayableNodeType Type => type;
        public string RuntimeKey => runtimeKey;
        public AnimationClip AnimationClip => animationClip;
        public RuntimeAnimatorController AnimatorController => animatorController;
        public int InputCount => inputCount;
        public IReadOnlyList<RuntimeAnimationLayer> Layers => layers;

        internal Playable CreatePlayable(UnityEngine.Playables.PlayableGraph graph, int nodeIndex)
        {
            switch (type)
            {
                case RuntimePlayableNodeType.AnimationClip:
                {
                    if (animationClip == null)
                    {
                        throw new InvalidOperationException(
                            $"Runtime node {nodeIndex} is an AnimationClipPlayable without an AnimationClip.");
                    }

                    var playable = AnimationClipPlayable.Create(graph, animationClip);
                    playable.SetDuration(animationClip.length);
                    playable.SetApplyFootIK(applyFootIK);
                    playable.SetApplyPlayableIK(applyPlayableIK);
                    return playable;
                }
                case RuntimePlayableNodeType.AnimationController:
                {
                    if (animatorController == null)
                    {
                        throw new InvalidOperationException(
                            $"Runtime node {nodeIndex} is an AnimationControllerPlayable without a controller.");
                    }

                    return AnimatorControllerPlayable.Create(graph, animatorController);
                }
                case RuntimePlayableNodeType.AnimationMixer:
                    return AnimationMixerPlayable.Create(graph, Mathf.Max(1, inputCount));
                case RuntimePlayableNodeType.AnimationLayerMixer:
                {
                    var playable = AnimationLayerMixerPlayable.Create(graph, Mathf.Max(1, inputCount));
                    for (var i = 0; i < layers.Length && i < inputCount; i++)
                    {
                        playable.SetLayerAdditive((uint)i, layers[i].IsAdditive);
                        if (layers[i].AvatarMask != null)
                        {
                            playable.SetLayerMaskFromAvatarMask((uint)i, layers[i].AvatarMask);
                        }
                    }

                    return playable;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported playable node type.");
            }
        }

#if UNITY_EDITOR
        public static RuntimePlayableNode CreateAnimationClip(
            AnimationClip clip,
            bool shouldApplyFootIK,
            bool shouldApplyPlayableIK)
        {
            return new RuntimePlayableNode
            {
                type = RuntimePlayableNodeType.AnimationClip,
                animationClip = clip,
                applyFootIK = shouldApplyFootIK,
                applyPlayableIK = shouldApplyPlayableIK
            };
        }

        public static RuntimePlayableNode CreateAnimationController(RuntimeAnimatorController controller)
        {
            return new RuntimePlayableNode
            {
                type = RuntimePlayableNodeType.AnimationController,
                animatorController = controller
            };
        }

        public static RuntimePlayableNode CreateAnimationMixer(string mixerRuntimeKey, int mixerInputCount)
        {
            return new RuntimePlayableNode
            {
                type = RuntimePlayableNodeType.AnimationMixer,
                runtimeKey = mixerRuntimeKey,
                inputCount = mixerInputCount
            };
        }

        public static RuntimePlayableNode CreateAnimationLayerMixer(
            string mixerRuntimeKey,
            int mixerInputCount,
            RuntimeAnimationLayer[] animationLayers)
        {
            return new RuntimePlayableNode
            {
                type = RuntimePlayableNodeType.AnimationLayerMixer,
                runtimeKey = mixerRuntimeKey,
                inputCount = mixerInputCount,
                layers = animationLayers ?? Array.Empty<RuntimeAnimationLayer>()
            };
        }
#endif
    }
}
