using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that moves a transform from start to end position using tweening.
    /// Uses AnimationCurve for easing - no external dependencies.
    /// </summary>
    [Serializable]
    public class TweenMoveCommand : BaseCommand<Transform>
    {
        [SerializeField]
        private ProxyObjectSaver<Transform> _targetTransform = new ProxyObjectSaver<Transform>();

        [SerializeField]
        private ProxyObjectSaver<Transform> _startPosition = new ProxyObjectSaver<Transform>();

        [SerializeField]
        private ProxyObjectSaver<Transform> _endPosition = new ProxyObjectSaver<Transform>();

        [SerializeField]
        private float _duration = 1f;

        [SerializeField]
        private AnimationCurve _curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField]
        private bool _useLocalPosition = false;

        [SerializeField]
        private bool _setStartPositionOnExecute = true;

        private bool _isCancelled;
        private Transform _runtimeTarget;

        public Transform TargetTransform
        {
            get => _targetTransform.Value;
            set => _targetTransform.Value = value;
        }

        public Transform StartPosition
        {
            get => _startPosition.Value;
            set => _startPosition.Value = value;
        }

        public Transform EndPosition
        {
            get => _endPosition.Value;
            set => _endPosition.Value = value;
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

        public bool UseLocalPosition
        {
            get => _useLocalPosition;
            set => _useLocalPosition = value;
        }

        public TweenMoveCommand()
        {
        }

        public TweenMoveCommand(Transform target, Transform start, Transform end, float duration)
        {
            _targetTransform = new ProxyObjectSaver<Transform>(true) { Value = target };
            _startPosition = new ProxyObjectSaver<Transform>(true) { Value = start };
            _endPosition = new ProxyObjectSaver<Transform>(true) { Value = end };
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
                Debug.LogWarning("[TweenMoveCommand] Target transform is null.");
                return;
            }

            if (_startPosition.Value == null || _endPosition.Value == null)
            {
                Debug.LogWarning("[TweenMoveCommand] Start or End position is null.");
                return;
            }

            Vector3 startPos = _useLocalPosition ? _startPosition.Value.localPosition : _startPosition.Value.position;
            Vector3 endPos = _useLocalPosition ? _endPosition.Value.localPosition : _endPosition.Value.position;

            if (_setStartPositionOnExecute)
            {
                if (_useLocalPosition)
                    target.localPosition = startPos;
                else
                    target.position = startPos;
            }

            float elapsed = 0f;

            while (elapsed < _duration && !_isCancelled)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _duration);
                float curveValue = _curve.Evaluate(t);

                Vector3 newPos = Vector3.LerpUnclamped(startPos, endPos, curveValue);

                if (_useLocalPosition)
                    target.localPosition = newPos;
                else
                    target.position = newPos;

                await Task.Yield();
            }

            // Ensure we end at the exact end position
            if (!_isCancelled)
            {
                if (_useLocalPosition)
                    target.localPosition = endPos;
                else
                    target.position = endPos;
            }
        }

        public override void Stop()
        {
            _isCancelled = true;
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _targetTransform.Save(saver);
            _startPosition.Save(saver);
            _endPosition.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _targetTransform.Restore(saver);
            _startPosition.Restore(saver);
            _endPosition.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _targetTransform.ReleaseResources(saver);
            _startPosition.ReleaseResources(saver);
            _endPosition.ReleaseResources(saver);
        }
    }
}
