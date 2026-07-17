using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that plays another scenario.
    /// Can be used to chain or nest scenarios.
    /// </summary>
    [Serializable]
    public class PlayAnotherScenarioCommand : BaseCommand
    {
        [SerializeField]
        private ProxyObjectSaver<Scenario> _scenario = new ProxyObjectSaver<Scenario>();

        [SerializeField]
        private bool _waitForCompletion = true;

        public Scenario Scenario
        {
            get => _scenario.Value;
            set => _scenario.Value = value;
        }

        public bool WaitForCompletion
        {
            get => _waitForCompletion;
            set => _waitForCompletion = value;
        }

        public PlayAnotherScenarioCommand()
        {
        }

        public PlayAnotherScenarioCommand(Scenario scenario, bool waitForCompletion = true)
        {
            _scenario = new ProxyObjectSaver<Scenario>(true) { Value = scenario };
            _waitForCompletion = waitForCompletion;
        }

        public override async Task Execute()
        {
            if (_scenario.Value == null)
            {
                Debug.LogWarning("[PlayAnotherScenarioCommand] Scenario is null.");
                return;
            }

            if (_waitForCompletion)
            {
                await _scenario.Value.ExecuteCommands();
            }
            else
            {
                _scenario.Value.Execute();
            }
        }

        public override void Stop()
        {
            if (_scenario.Value != null && _scenario.Value.IsInExecution)
            {
                _scenario.Value.CancelExecution();
            }
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _scenario.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _scenario.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _scenario.ReleaseResources(saver);
        }
    }
}
