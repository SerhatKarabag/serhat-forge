using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that instantly updates a transform's position.
    /// No animation - just an immediate position change.
    /// </summary>
    [Serializable]
    public class UpdatePositionCommand : BaseCommand<Transform>
    {
        [SerializeField]
        private ProxyObjectSaver<Transform> _targetTransform = new ProxyObjectSaver<Transform>();

        [SerializeField]
        private ProxyObjectSaver<Transform> _positionReference = new ProxyObjectSaver<Transform>();

        [SerializeField]
        private Vector3 _position;

        [SerializeField]
        private bool _usePositionReference = true;

        [SerializeField]
        private bool _useLocalPosition = false;

        private Transform _runtimeTarget;

        public Transform TargetTransform
        {
            get => _targetTransform.Value;
            set => _targetTransform.Value = value;
        }

        public Transform PositionReference
        {
            get => _positionReference.Value;
            set => _positionReference.Value = value;
        }

        public Vector3 Position
        {
            get => _position;
            set => _position = value;
        }

        public bool UsePositionReference
        {
            get => _usePositionReference;
            set => _usePositionReference = value;
        }

        public bool UseLocalPosition
        {
            get => _useLocalPosition;
            set => _useLocalPosition = value;
        }

        public UpdatePositionCommand()
        {
        }

        public UpdatePositionCommand(Transform target, Vector3 position, bool useLocal = false)
        {
            _targetTransform = new ProxyObjectSaver<Transform>(true) { Value = target };
            _position = position;
            _usePositionReference = false;
            _useLocalPosition = useLocal;
        }

        public UpdatePositionCommand(Transform target, Transform positionRef, bool useLocal = false)
        {
            _targetTransform = new ProxyObjectSaver<Transform>(true) { Value = target };
            _positionReference = new ProxyObjectSaver<Transform>(true) { Value = positionRef };
            _usePositionReference = true;
            _useLocalPosition = useLocal;
        }

        public override void SetParameter(Transform parameter)
        {
            _runtimeTarget = parameter;
        }

        public override Task Execute()
        {
            var target = _runtimeTarget ?? _targetTransform.Value;

            if (target == null)
            {
                Debug.LogWarning("[UpdatePositionCommand] Target transform is null.");
                return Task.CompletedTask;
            }

            Vector3 newPosition;

            if (_usePositionReference && _positionReference.Value != null)
            {
                newPosition = _useLocalPosition
                    ? _positionReference.Value.localPosition
                    : _positionReference.Value.position;
            }
            else
            {
                newPosition = _position;
            }

            if (_useLocalPosition)
                target.localPosition = newPosition;
            else
                target.position = newPosition;

            return Task.CompletedTask;
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _targetTransform.Save(saver);
            _positionReference.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _targetTransform.Restore(saver);
            _positionReference.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _targetTransform.ReleaseResources(saver);
            _positionReference.ReleaseResources(saver);
        }
    }
}
