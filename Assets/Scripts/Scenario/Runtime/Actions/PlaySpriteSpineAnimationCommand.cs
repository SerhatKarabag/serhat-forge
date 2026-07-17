using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that plays a Spine animation on a SkeletonGraphic (UI) component.
    /// Uses reflection to avoid hard dependency on Spine runtime.
    /// Works with SkeletonGraphic component for UI-based Spine animations.
    /// </summary>
    [Serializable]
    public class PlaySpriteSpineAnimationCommand : BaseCommand
    {
        [SerializeField]
        private ProxyObjectSaver<Component> _skeletonGraphic = new ProxyObjectSaver<Component>();

        [SerializeField]
        private string _animationName = "";

        [SerializeField]
        private bool _loop = false;

        [SerializeField]
        private int _trackIndex = 0;

        [SerializeField]
        private float _timeScale = 1f;

        [SerializeField]
        private float _mixDuration = 0.2f;

        [SerializeField]
        private bool _waitForComplete = true;

        [SerializeField]
        private bool _freeze = false;

        private bool _isCancelled;
        private bool _animationComplete;

        public Component SkeletonGraphic
        {
            get => _skeletonGraphic.Value;
            set => _skeletonGraphic.Value = value;
        }

        public string AnimationName
        {
            get => _animationName;
            set => _animationName = value;
        }

        public bool Loop
        {
            get => _loop;
            set => _loop = value;
        }

        public int TrackIndex
        {
            get => _trackIndex;
            set => _trackIndex = value;
        }

        public bool Freeze
        {
            get => _freeze;
            set => _freeze = value;
        }

        public PlaySpriteSpineAnimationCommand()
        {
        }

        public PlaySpriteSpineAnimationCommand(Component skeletonGraphic, string animationName, bool loop = false)
        {
            _skeletonGraphic = new ProxyObjectSaver<Component>(true) { Value = skeletonGraphic };
            _animationName = animationName;
            _loop = loop;
        }

        public override async Task Execute()
        {
            _isCancelled = false;
            _animationComplete = false;

            if (_skeletonGraphic.Value == null)
            {
                Debug.LogWarning("[PlaySpriteSpineAnimationCommand] SkeletonGraphic is null.");
                return;
            }

            if (string.IsNullOrEmpty(_animationName))
            {
                Debug.LogWarning("[PlaySpriteSpineAnimationCommand] Animation name is empty.");
                return;
            }

            var component = _skeletonGraphic.Value;
            var type = component.GetType();

            // Set freeze state if needed
            if (_freeze)
            {
                var freezeProperty = type.GetProperty("freeze");
                if (freezeProperty != null)
                {
                    freezeProperty.SetValue(component, false); // Unfreeze to play
                }
            }

            // Try to find AnimationState property
            var animationStateProperty = type.GetProperty("AnimationState") ?? type.GetProperty("state");

            if (animationStateProperty == null)
            {
                Debug.LogWarning($"[PlaySpriteSpineAnimationCommand] Could not find AnimationState on {type.Name}");
                return;
            }

            var animationState = animationStateProperty.GetValue(component);
            if (animationState == null)
            {
                Debug.LogWarning("[PlaySpriteSpineAnimationCommand] AnimationState is null.");
                return;
            }

            var stateType = animationState.GetType();

            // Set animation
            var setAnimationMethod = stateType.GetMethod("SetAnimation",
                new Type[] { typeof(int), typeof(string), typeof(bool) });

            if (setAnimationMethod == null)
            {
                Debug.LogWarning("[PlaySpriteSpineAnimationCommand] Could not find SetAnimation method.");
                return;
            }

            var trackEntry = setAnimationMethod.Invoke(animationState, new object[] { _trackIndex, _animationName, _loop });

            if (trackEntry != null)
            {
                var trackEntryType = trackEntry.GetType();

                // Set time scale
                var timeScaleProperty = trackEntryType.GetProperty("TimeScale");
                if (timeScaleProperty != null)
                {
                    timeScaleProperty.SetValue(trackEntry, _timeScale);
                }

                // Set mix duration
                var mixDurationProperty = trackEntryType.GetProperty("MixDuration");
                if (mixDurationProperty != null)
                {
                    mixDurationProperty.SetValue(trackEntry, _mixDuration);
                }

                // Subscribe to complete event if we need to wait
                if (_waitForComplete && !_loop)
                {
                    var completeEvent = trackEntryType.GetEvent("Complete");
                    if (completeEvent != null)
                    {
                        var handler = CreateCompleteHandler(completeEvent.EventHandlerType);
                        completeEvent.AddEventHandler(trackEntry, handler);
                    }
                }
            }

            // Wait for animation to complete
            if (_waitForComplete && !_loop)
            {
                while (!_animationComplete && !_isCancelled)
                {
                    await Task.Yield();
                }
            }

            // Freeze after animation if needed
            if (_freeze && !_isCancelled)
            {
                var freezeProperty = type.GetProperty("freeze");
                if (freezeProperty != null)
                {
                    freezeProperty.SetValue(component, true);
                }
            }
        }

        private Delegate CreateCompleteHandler(Type eventHandlerType)
        {
            var method = GetType().GetMethod(nameof(OnAnimationComplete),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            return Delegate.CreateDelegate(eventHandlerType, this, method);
        }

        private void OnAnimationComplete(object trackEntry)
        {
            _animationComplete = true;
        }

        public override void Stop()
        {
            _isCancelled = true;
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _skeletonGraphic.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _skeletonGraphic.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _skeletonGraphic.ReleaseResources(saver);
        }
    }
}
