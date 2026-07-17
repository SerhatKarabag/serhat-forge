using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that freezes (locks) a transform's position for a specified duration.
    /// The transform will be forced to stay at its current or specified position.
    /// </summary>
    [Serializable]
    public class FreezePositionCommand : BaseCommand<Transform>
    {
        [SerializeField]
        private ProxyObjectSaver<Transform> _targetTransform = new ProxyObjectSaver<Transform>();

        [SerializeField]
        private float _duration = 1f;

        [SerializeField]
        private bool _freezeX = true;

        [SerializeField]
        private bool _freezeY = true;

        [SerializeField]
        private bool _freezeZ = true;

        [SerializeField]
        private bool _useSpecificPosition = false;

        [SerializeField]
        private Vector3 _freezePosition = Vector3.zero;

        [SerializeField]
        private bool _useLocalPosition = false;

        [SerializeField]
        private bool _infiniteDuration = false;

        private bool _isCancelled;
        private Transform _runtimeTarget;
        private Vector3 _frozenPosition;

        public Transform TargetTransform
        {
            get => _targetTransform.Value;
            set => _targetTransform.Value = value;
        }

        public float Duration
        {
            get => _duration;
            set => _duration = value;
        }

        public bool FreezeX
        {
            get => _freezeX;
            set => _freezeX = value;
        }

        public bool FreezeY
        {
            get => _freezeY;
            set => _freezeY = value;
        }

        public bool FreezeZ
        {
            get => _freezeZ;
            set => _freezeZ = value;
        }

        public bool InfiniteDuration
        {
            get => _infiniteDuration;
            set => _infiniteDuration = value;
        }

        public FreezePositionCommand()
        {
        }

        public FreezePositionCommand(Transform target, float duration, bool freezeX = true, bool freezeY = true, bool freezeZ = true)
        {
            _targetTransform = new ProxyObjectSaver<Transform>(true) { Value = target };
            _duration = duration;
            _freezeX = freezeX;
            _freezeY = freezeY;
            _freezeZ = freezeZ;
        }

        public override void SetParameter(Transform parameter)
        {
            _runtimeTarget = parameter;
        }

        public override async Task Execute()
        {
            _isCancelled = false;

            var target = _runtimeTarget ?? _targetTransform.Value;

            if (target == null)
            {
                Debug.LogWarning("[FreezePositionCommand] Target transform is null.");
                return;
            }

            // Determine freeze position
            if (_useSpecificPosition)
            {
                _frozenPosition = _freezePosition;
            }
            else
            {
                _frozenPosition = _useLocalPosition ? target.localPosition : target.position;
            }

            float elapsed = 0f;

            while (!_isCancelled && (_infiniteDuration || elapsed < _duration))
            {
                ApplyFreeze(target);

                await Task.Yield();
                elapsed += Time.deltaTime;
            }
        }

        private void ApplyFreeze(Transform target)
        {
            Vector3 currentPos = _useLocalPosition ? target.localPosition : target.position;
            Vector3 newPos = currentPos;

            if (_freezeX) newPos.x = _frozenPosition.x;
            if (_freezeY) newPos.y = _frozenPosition.y;
            if (_freezeZ) newPos.z = _frozenPosition.z;

            if (_useLocalPosition)
                target.localPosition = newPos;
            else
                target.position = newPos;
        }

        public override void Stop()
        {
            _isCancelled = true;
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _targetTransform.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _targetTransform.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _targetTransform.ReleaseResources(saver);
        }
    }
}
