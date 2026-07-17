using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that invokes a UnityEvent.
    /// Useful for calling arbitrary methods without writing custom commands.
    /// </summary>
    [Serializable]
    public class UnityEventCommand : BaseCommand
    {
        [SerializeField]
        private UnityEvent _event = new UnityEvent();

        public UnityEvent Event => _event;

        public UnityEventCommand()
        {
        }

        public override Task Execute()
        {
            _event?.Invoke();
            return Task.CompletedTask;
        }
    }
}
