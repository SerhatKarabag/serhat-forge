using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that plays a legacy Animation clip.
    /// Works with the legacy Animation component (not Animator).
    /// </summary>
    [Serializable]
    public class PlayLegacyAnimationCommand : BaseCommand
    {
        public enum PlayMode
        {
            Play,
            CrossFade,
            PlayQueued,
            Blend
        }

        [SerializeField]
        private ProxyObjectSaver<Animation> _animation = new ProxyObjectSaver<Animation>();

        [SerializeField]
        private string _clipName = "";

        [SerializeField]
        private PlayMode _playMode = PlayMode.Play;

        [SerializeField]
        private float _crossFadeTime = 0.3f;

        [SerializeField]
        private float _targetWeight = 1f;

        [SerializeField]
        private bool _waitForComplete = true;

        private bool _isCancelled;

        public Animation Animation
        {
            get => _animation.Value;
            set => _animation.Value = value;
        }

        public string ClipName
        {
            get => _clipName;
            set => _clipName = value;
        }

        public PlayMode Mode
        {
            get => _playMode;
            set => _playMode = value;
        }

        public float CrossFadeTime
        {
            get => _crossFadeTime;
            set => _crossFadeTime = value;
        }

        public PlayLegacyAnimationCommand()
        {
        }

        public PlayLegacyAnimationCommand(Animation animation, string clipName, PlayMode mode = PlayMode.Play)
        {
            _animation = new ProxyObjectSaver<Animation>(true) { Value = animation };
            _clipName = clipName;
            _playMode = mode;
        }

        public override async Task Execute()
        {
            _isCancelled = false;

            if (_animation.Value == null)
            {
                Debug.LogWarning("[PlayLegacyAnimationCommand] Animation component is null.");
                return;
            }

            if (string.IsNullOrEmpty(_clipName))
            {
                Debug.LogWarning("[PlayLegacyAnimationCommand] Clip name is empty.");
                return;
            }

            var anim = _animation.Value;
            var clip = anim.GetClip(_clipName);

            if (clip == null)
            {
                Debug.LogWarning($"[PlayLegacyAnimationCommand] Clip '{_clipName}' not found.");
                return;
            }

            float duration = clip.length;

            switch (_playMode)
            {
                case PlayMode.Play:
                    anim.Play(_clipName);
                    break;

                case PlayMode.CrossFade:
                    anim.CrossFade(_clipName, _crossFadeTime);
                    break;

                case PlayMode.PlayQueued:
                    anim.PlayQueued(_clipName);
                    break;

                case PlayMode.Blend:
                    anim.Blend(_clipName, _targetWeight, _crossFadeTime);
                    break;
            }

            if (_waitForComplete)
            {
                float elapsed = 0f;

                while (elapsed < duration && !_isCancelled)
                {
                    await Task.Yield();
                    elapsed += Time.deltaTime;

                    // Also check if animation is still playing
                    if (!anim.IsPlaying(_clipName))
                        break;
                }
            }
        }

        public override void Stop()
        {
            _isCancelled = true;

            if (_animation.Value != null)
            {
                _animation.Value.Stop(_clipName);
            }
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _animation.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _animation.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _animation.ReleaseResources(saver);
        }
    }
}
