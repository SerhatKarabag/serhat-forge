using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that enables or disables a Behaviour component.
    /// </summary>
    [Serializable]
    public class SetComponentEnabledCommand : BaseCommand
    {
        [SerializeField]
        private ProxyObjectSaver<Behaviour> _targetComponent = new ProxyObjectSaver<Behaviour>();

        [SerializeField]
        private bool _isEnabled = true;

        public Behaviour TargetComponent
        {
            get => _targetComponent.Value;
            set => _targetComponent.Value = value;
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        public SetComponentEnabledCommand()
        {
        }

        public SetComponentEnabledCommand(Behaviour target, bool enabled)
        {
            _targetComponent = new ProxyObjectSaver<Behaviour>(true) { Value = target };
            _isEnabled = enabled;
        }

        public override Task Execute()
        {
            if (_targetComponent.Value != null)
            {
                _targetComponent.Value.enabled = _isEnabled;
            }
            else
            {
                Debug.LogWarning("[SetComponentEnabledCommand] Target component is null.");
            }

            return Task.CompletedTask;
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _targetComponent.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _targetComponent.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _targetComponent.ReleaseResources(saver);
        }
    }
}
