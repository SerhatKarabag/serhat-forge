using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that scales a transform from start to end scale using tweening.
    /// </summary>
    [Serializable]
    public class TweenScaleCommand : BaseCommand<Transform>
    {
        [SerializeField]
        private ProxyObjectSaver<Transform> _targetTransform = new ProxyObjectSaver<Transform>();

        [SerializeField]
        private Vector3 _startScale = Vector3.one;

        [SerializeField]
        private Vector3 _endScale = Vector3.one;

        [SerializeField]
        private float _duration = 1f;

        [SerializeField]
        private AnimationCurve _curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField]
        private bool _setStartScaleOnExecute = true;

        private bool _isCancelled;
        private Transform _runtimeTarget;

        public Transform TargetTransform
        {
            get => _targetTransform.Value;
            set => _targetTransform.Value = value;
        }

        public Vector3 StartScale
        {
            get => _startScale;
            set => _startScale = value;
        }

        public Vector3 EndScale
        {
            get => _endScale;
            set => _endScale = value;
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

        public TweenScaleCommand()
        {
        }

        public TweenScaleCommand(Transform target, Vector3 startScale, Vector3 endScale, float duration)
        {
            _targetTransform = new ProxyObjectSaver<Transform>(true) { Value = target };
            _startScale = startScale;
            _endScale = endScale;
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
                Debug.LogWarning("[TweenScaleCommand] Target transform is null.");
                return;
            }

            if (_setStartScaleOnExecute)
            {
                target.localScale = _startScale;
            }

            float elapsed = 0f;

            while (elapsed < _duration && !_isCancelled)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _duration);
                float curveValue = _curve.Evaluate(t);

                target.localScale = Vector3.LerpUnclamped(_startScale, _endScale, curveValue);

                await Task.Yield();
            }

            if (!_isCancelled)
            {
                target.localScale = _endScale;
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
