using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that plays an animation on an Animator component.
    /// Can play by state name or trigger a parameter.
    /// </summary>
    [Serializable]
    public class PlayAnimatorCommand : BaseCommand
    {
        [SerializeField]
        private ProxyObjectSaver<Animator> _animator = new ProxyObjectSaver<Animator>();

        [SerializeField]
        private string _animationName;

        [SerializeField]
        private bool _isTrigger;

        [SerializeField]
        private bool _waitForAnimation = true;

        [SerializeField]
        private int _layer = 0;

        private bool _isCancelled;

        public Animator Animator
        {
            get => _animator.Value;
            set => _animator.Value = value;
        }

        public string AnimationName
        {
            get => _animationName;
            set => _animationName = value;
        }

        public bool IsTrigger
        {
            get => _isTrigger;
            set => _isTrigger = value;
        }

        public bool WaitForAnimation
        {
            get => _waitForAnimation;
            set => _waitForAnimation = value;
        }

        public PlayAnimatorCommand()
        {
        }

        public PlayAnimatorCommand(Animator animator, string animationName, bool isTrigger = false)
        {
            _animator = new ProxyObjectSaver<Animator>(true) { Value = animator };
            _animationName = animationName;
            _isTrigger = isTrigger;
        }

        public override async Task Execute()
        {
            _isCancelled = false;

            if (_animator.Value == null)
            {
                Debug.LogWarning("[PlayAnimatorCommand] Animator is null.");
                return;
            }

            if (string.IsNullOrEmpty(_animationName))
            {
                Debug.LogWarning("[PlayAnimatorCommand] Animation name is empty.");
                return;
            }

            if (_isTrigger)
            {
                _animator.Value.SetTrigger(_animationName);
            }
            else
            {
                _animator.Value.Play(_animationName, _layer);
            }

            if (_waitForAnimation)
            {
                // Wait for the animation to start
                await Task.Yield();

                // Wait for the animation to complete
                while (!_isCancelled)
                {
                    var stateInfo = _animator.Value.GetCurrentAnimatorStateInfo(_layer);

                    if (stateInfo.normalizedTime >= 1f && !_animator.Value.IsInTransition(_layer))
                    {
                        break;
                    }

                    await Task.Yield();
                }
            }
        }

        public override void Stop()
        {
            _isCancelled = true;
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _animator.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _animator.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _animator.ReleaseResources(saver);
        }
    }
}
