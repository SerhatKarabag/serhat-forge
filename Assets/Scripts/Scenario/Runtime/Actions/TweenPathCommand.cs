using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that moves a transform along a path defined by waypoints.
    /// Uses Catmull-Rom spline for smooth interpolation.
    /// </summary>
    [Serializable]
    public class TweenPathCommand : BaseCommand<Transform>
    {
        public enum PathType
        {
            Linear,
            CatmullRom
        }

        public enum PathMode
        {
            Full3D,
            TopDown2D,
            Sidescroller2D
        }

        [SerializeField]
        private ProxyObjectSaver<Transform> _targetTransform = new ProxyObjectSaver<Transform>();

        [SerializeField]
        private List<Vector3> _waypoints = new List<Vector3>();

        [SerializeField]
        private float _duration = 1f;

        [SerializeField]
        private PathType _pathType = PathType.CatmullRom;

        [SerializeField]
        private PathMode _pathMode = PathMode.Full3D;

        [SerializeField]
        private AnimationCurve _curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField]
        private bool _closedPath = false;

        [SerializeField]
        private bool _useLocalPosition = false;

        [SerializeField]
        private bool _lookAtPath = false;

        [SerializeField]
        private float _lookAhead = 0.01f;

        private bool _isCancelled;
        private Transform _runtimeTarget;

        public Transform TargetTransform
        {
            get => _targetTransform.Value;
            set => _targetTransform.Value = value;
        }

        public List<Vector3> Waypoints
        {
            get => _waypoints;
            set => _waypoints = value;
        }

        public float Duration
        {
            get => _duration;
            set => _duration = value;
        }

        public TweenPathCommand()
        {
        }

        public TweenPathCommand(Transform target, List<Vector3> waypoints, float duration)
        {
            _targetTransform = new ProxyObjectSaver<Transform>(true) { Value = target };
            _waypoints = waypoints;
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
                Debug.LogWarning("[TweenPathCommand] Target transform is null.");
                return;
            }

            if (_waypoints == null || _waypoints.Count < 2)
            {
                Debug.LogWarning("[TweenPathCommand] Need at least 2 waypoints.");
                return;
            }

            float elapsed = 0f;

            while (elapsed < _duration && !_isCancelled)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _duration);
                float curveValue = _curve.Evaluate(t);

                Vector3 position = GetPointOnPath(curveValue);

                if (_useLocalPosition)
                    target.localPosition = position;
                else
                    target.position = position;

                // Look at path direction
                if (_lookAtPath)
                {
                    float lookT = Mathf.Min(curveValue + _lookAhead, 1f);
                    Vector3 lookAtPos = GetPointOnPath(lookT);
                    Vector3 direction = (lookAtPos - position).normalized;

                    if (direction != Vector3.zero)
                    {
                        Quaternion targetRotation = Quaternion.LookRotation(direction);

                        switch (_pathMode)
                        {
                            case PathMode.TopDown2D:
                                targetRotation = Quaternion.Euler(0, targetRotation.eulerAngles.y, 0);
                                break;
                            case PathMode.Sidescroller2D:
                                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                                targetRotation = Quaternion.Euler(0, 0, angle);
                                break;
                        }

                        if (_useLocalPosition)
                            target.localRotation = targetRotation;
                        else
                            target.rotation = targetRotation;
                    }
                }

                await Task.Yield();
            }

            // Ensure we end at the last waypoint
            if (!_isCancelled)
            {
                Vector3 finalPos = _closedPath ? _waypoints[0] : _waypoints[_waypoints.Count - 1];
                if (_useLocalPosition)
                    target.localPosition = finalPos;
                else
                    target.position = finalPos;
            }
        }

        private Vector3 GetPointOnPath(float t)
        {
            if (_pathType == PathType.Linear)
            {
                return GetLinearPoint(t);
            }
            else
            {
                return GetCatmullRomPoint(t);
            }
        }

        private Vector3 GetLinearPoint(float t)
        {
            int count = _closedPath ? _waypoints.Count : _waypoints.Count - 1;
            float segmentT = t * count;
            int segmentIndex = Mathf.FloorToInt(segmentT);
            segmentIndex = Mathf.Clamp(segmentIndex, 0, count - 1);

            float localT = segmentT - segmentIndex;

            int nextIndex = (segmentIndex + 1) % _waypoints.Count;

            return Vector3.Lerp(_waypoints[segmentIndex], _waypoints[nextIndex], localT);
        }

        private Vector3 GetCatmullRomPoint(float t)
        {
            int count = _closedPath ? _waypoints.Count : _waypoints.Count - 1;
            float segmentT = t * count;
            int segmentIndex = Mathf.FloorToInt(segmentT);
            segmentIndex = Mathf.Clamp(segmentIndex, 0, count - 1);

            float localT = segmentT - segmentIndex;

            // Get 4 points for Catmull-Rom
            int p0 = segmentIndex - 1;
            int p1 = segmentIndex;
            int p2 = segmentIndex + 1;
            int p3 = segmentIndex + 2;

            if (_closedPath)
            {
                p0 = (p0 + _waypoints.Count) % _waypoints.Count;
                p1 = p1 % _waypoints.Count;
                p2 = p2 % _waypoints.Count;
                p3 = p3 % _waypoints.Count;
            }
            else
            {
                p0 = Mathf.Clamp(p0, 0, _waypoints.Count - 1);
                p1 = Mathf.Clamp(p1, 0, _waypoints.Count - 1);
                p2 = Mathf.Clamp(p2, 0, _waypoints.Count - 1);
                p3 = Mathf.Clamp(p3, 0, _waypoints.Count - 1);
            }

            return CatmullRom(_waypoints[p0], _waypoints[p1], _waypoints[p2], _waypoints[p3], localT);
        }

        private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
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
