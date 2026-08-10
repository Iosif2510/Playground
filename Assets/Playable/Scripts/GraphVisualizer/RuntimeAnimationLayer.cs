using System;
using UnityEngine;

namespace Playground.Playables
{
    [Serializable]
    public struct RuntimeAnimationLayer
    {
        [SerializeField] private AvatarMask avatarMask;
        [SerializeField] private bool isAdditive;

        public AvatarMask AvatarMask => avatarMask;
        public bool IsAdditive => isAdditive;

        public RuntimeAnimationLayer(AvatarMask avatarMask, bool isAdditive)
        {
            this.avatarMask = avatarMask;
            this.isAdditive = isAdditive;
        }
    }
}
