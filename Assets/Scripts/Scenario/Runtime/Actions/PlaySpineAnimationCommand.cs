using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that plays a Spine animation.
    /// Uses reflection to avoid hard dependency on Spine runtime.
    /// Works with SkeletonAnimation component.
    /// </summary>
    [Serializable]
    public class PlaySpineAnimationCommand : BaseCommand
    {
        [SerializeField]
        private ProxyObjectSaver<Component> _skeletonAnimation = new ProxyObjectSaver<Component>();

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

        private bool _isCancelled;
        private bool _animationComplete;

        public Component SkeletonAnimation
        {
            get => _skeletonAnimation.Value;
            set => _skeletonAnimation.Value = value;
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

        public PlaySpineAnimationCommand()
        {
        }

        public PlaySpineAnimationCommand(Component skeletonAnimation, string animationName, bool loop = false)
        {
            _skeletonAnimation = new ProxyObjectSaver<Component>(true) { Value = skeletonAnimation };
            _animationName = animationName;
            _loop = loop;
        }

        public override async Task Execute()
        {
            _isCancelled = false;
            _animationComplete = false;

            if (_skeletonAnimation.Value == null)
            {
                Debug.LogWarning("[PlaySpineAnimationCommand] SkeletonAnimation is null.");
                return;
            }

            if (string.IsNullOrEmpty(_animationName))
            {
                Debug.LogWarning("[PlaySpineAnimationCommand] Animation name is empty.");
                return;
            }

            var component = _skeletonAnimation.Value;
            var type = component.GetType();

            // Try to find AnimationState property
            var animationStateProperty = type.GetProperty("AnimationState") ?? type.GetProperty("state");

            if (animationStateProperty == null)
            {
                Debug.LogWarning($"[PlaySpineAnimationCommand] Could not find AnimationState on {type.Name}");
                return;
            }

            var animationState = animationStateProperty.GetValue(component);
            if (animationState == null)
            {
                Debug.LogWarning("[PlaySpineAnimationCommand] AnimationState is null.");
                return;
            }

            var stateType = animationState.GetType();

            // Set animation
            var setAnimationMethod = stateType.GetMethod("SetAnimation",
                new Type[] { typeof(int), typeof(string), typeof(bool) });

            if (setAnimationMethod == null)
            {
                Debug.LogWarning("[PlaySpineAnimationCommand] Could not find SetAnimation method.");
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
        }

        private Delegate CreateCompleteHandler(Type eventHandlerType)
        {
            // Create a delegate that sets _animationComplete = true
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
            _skeletonAnimation.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _skeletonAnimation.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _skeletonAnimation.ReleaseResources(saver);
        }
    }
}
