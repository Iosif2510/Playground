using UnityEngine;
using UnityEngine.Playables;

namespace Playground.Playables
{
    /// <summary>Creates and owns a RuntimePlayableGraph for this GameObject's Animator.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class RuntimePlayableGraphPlayer : MonoBehaviour
    {
        [SerializeField] private RuntimePlayableGraph graphAsset;
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private DirectorUpdateMode updateMode = DirectorUpdateMode.GameTime;
        [SerializeField] private bool playOnEnable = true;

        private RuntimePlayableGraphInstance instance;

        public RuntimePlayableGraph GraphAsset => graphAsset;
        public RuntimePlayableGraphInstance Instance => instance;

        private void Reset()
        {
            targetAnimator = GetComponent<Animator>();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || graphAsset == null)
            {
                return;
            }

            Rebuild();
        }

        private void OnDisable()
        {
            DisposeInstance();
        }

        public void Rebuild()
        {
            DisposeInstance();

            if (graphAsset == null)
            {
                return;
            }

            if (targetAnimator == null)
            {
                targetAnimator = GetComponent<Animator>();
            }

            instance = graphAsset.CreateInstance(targetAnimator, updateMode);
            if (playOnEnable)
            {
                instance.Play();
            }
        }

        public void Play()
        {
            instance?.Play();
        }

        public void Stop()
        {
            instance?.Stop();
        }

        private void DisposeInstance()
        {
            instance?.Dispose();
            instance = null;
        }
    }
}
