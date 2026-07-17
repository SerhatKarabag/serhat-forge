using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that calls a method on a MonoBehaviour using reflection.
    /// Supports parameterless methods only.
    /// </summary>
    [Serializable]
    public class CallMethodCommand : BaseCommand
    {
        [SerializeField]
        private ProxyObjectSaver<MonoBehaviour> _target = new ProxyObjectSaver<MonoBehaviour>();

        [SerializeField]
        private string _methodName;

        public MonoBehaviour Target
        {
            get => _target.Value;
            set => _target.Value = value;
        }

        public string MethodName
        {
            get => _methodName;
            set => _methodName = value;
        }

        public CallMethodCommand()
        {
        }

        public CallMethodCommand(MonoBehaviour target, string methodName)
        {
            _target = new ProxyObjectSaver<MonoBehaviour>(true) { Value = target };
            _methodName = methodName;
        }

        public override Task Execute()
        {
            if (_target.Value == null)
            {
                Debug.LogWarning("[CallMethodCommand] Target is null.");
                return Task.CompletedTask;
            }

            if (string.IsNullOrEmpty(_methodName))
            {
                Debug.LogWarning("[CallMethodCommand] Method name is empty.");
                return Task.CompletedTask;
            }

            var type = _target.Value.GetType();
            var method = type.GetMethod(_methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (method == null)
            {
                Debug.LogWarning($"[CallMethodCommand] Method '{_methodName}' not found on {type.Name}.");
                return Task.CompletedTask;
            }

            try
            {
                method.Invoke(_target.Value, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CallMethodCommand] Error invoking method '{_methodName}': {e}");
            }

            return Task.CompletedTask;
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _target.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _target.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _target.ReleaseResources(saver);
        }
    }
}
