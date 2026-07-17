using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that activates or deactivates a GameObject.
    /// </summary>
    [Serializable]
    public class SetGameObjectActiveCommand : BaseCommand
    {
        [SerializeField]
        private ProxyObjectSaver<GameObject> _targetObject = new ProxyObjectSaver<GameObject>();

        [SerializeField]
        private bool _isActive = true;

        [SerializeField]
        [Tooltip("If enabled and Is Active is true, target will be set inactive after the delay.")]
        private bool _autoDeactivateAfterDelay;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Seconds to wait before auto-deactivating the target object.")]
        private float _autoDeactivateDelay = 1f;

        private int _executionToken;

        public GameObject TargetObject
        {
            get => _targetObject.Value;
            set => _targetObject.Value = value;
        }

        public bool IsActive
        {
            get => _isActive;
            set => _isActive = value;
        }

        public bool AutoDeactivateAfterDelay
        {
            get => _autoDeactivateAfterDelay;
            set => _autoDeactivateAfterDelay = value;
        }

        public float AutoDeactivateDelay
        {
            get => _autoDeactivateDelay;
            set => _autoDeactivateDelay = Mathf.Max(0f, value);
        }

        public SetGameObjectActiveCommand()
        {
        }

        public SetGameObjectActiveCommand(GameObject target, bool active)
        {
            _targetObject = new ProxyObjectSaver<GameObject>(true) { Value = target };
            _isActive = active;
        }

        public override async Task Execute()
        {
            var target = _targetObject.Value;
            if (target != null)
            {
                target.SetActive(_isActive);

                if (_isActive && _autoDeactivateAfterDelay)
                {
                    var token = ++_executionToken;
                    var delaySeconds = Mathf.Max(0f, _autoDeactivateDelay);
                    var elapsed = 0f;

                    while (elapsed < delaySeconds && token == _executionToken)
                    {
                        await Task.Yield();
                        elapsed += Time.unscaledDeltaTime;
                    }

                    if (token == _executionToken && target != null)
                    {
                        target.SetActive(false);
                    }
                }
            }
            else
            {
                Debug.LogWarning("[SetGameObjectActiveCommand] Target object is null.");
            }
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _targetObject.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _targetObject.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _targetObject.ReleaseResources(saver);
        }

        public override void Stop()
        {
            _executionToken++;
        }
    }
}
