using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that rotates a transform from start to end rotation using tweening.
    /// </summary>
    [Serializable]
    public class TweenRotateCommand : BaseCommand<Transform>
    {
        [SerializeField]
        private ProxyObjectSaver<Transform> _targetTransform = new ProxyObjectSaver<Transform>();

        [SerializeField]
        private Vector3 _startRotation = Vector3.zero;

        [SerializeField]
        private Vector3 _endRotation = Vector3.zero;

        [SerializeField]
        private float _duration = 1f;

        [SerializeField]
        private AnimationCurve _curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField]
        private bool _useLocalRotation = true;

        [SerializeField]
        private bool _setStartRotationOnExecute = true;

        private bool _isCancelled;
        private Transform _runtimeTarget;

        public Transform TargetTransform
        {
            get => _targetTransform.Value;
            set => _targetTransform.Value = value;
        }

        public Vector3 StartRotation
        {
            get => _startRotation;
            set => _startRotation = value;
        }

        public Vector3 EndRotation
        {
            get => _endRotation;
            set => _endRotation = value;
        }

        public float Duration
        {
            get => _duration;
            set => _duration = value;
        }

        public AnimationCurve Curve
        {
            get => _curve;
            set => _curve = value;
        }

        public TweenRotateCommand()
        {
        }

        public TweenRotateCommand(Transform target, Vector3 startRotation, Vector3 endRotation, float duration)
        {
            _targetTransform = new ProxyObjectSaver<Transform>(true) { Value = target };
            _startRotation = startRotation;
            _endRotation = endRotation;
            _duration = duration;
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
                Debug.LogWarning("[TweenRotateCommand] Target transform is null.");
                return;
            }

            Quaternion startQuat = Quaternion.Euler(_startRotation);
            Quaternion endQuat = Quaternion.Euler(_endRotation);

            if (_setStartRotationOnExecute)
            {
                if (_useLocalRotation)
                    target.localRotation = startQuat;
                else
                    target.rotation = startQuat;
            }

            float elapsed = 0f;

            while (elapsed < _duration && !_isCancelled)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _duration);
                float curveValue = _curve.Evaluate(t);

                Quaternion newRot = Quaternion.SlerpUnclamped(startQuat, endQuat, curveValue);

                if (_useLocalRotation)
                    target.localRotation = newRot;
                else
                    target.rotation = newRot;

                await Task.Yield();
            }

            if (!_isCancelled)
            {
                if (_useLocalRotation)
                    target.localRotation = endQuat;
                else
                    target.rotation = endQuat;
            }
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
