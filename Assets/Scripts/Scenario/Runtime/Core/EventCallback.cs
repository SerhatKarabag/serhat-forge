using UnityEngine;
using UnityEngine.Events;

namespace ScenarioSystem
{
    /// <summary>
    /// Simple component that invokes a UnityEvent when Execute is called.
    /// Useful for triggering custom behavior from scenarios.
    /// </summary>
    public class EventCallback : MonoBehaviour
    {
        [SerializeField]
        private UnityEvent customEvent;

        /// <summary>
        /// Executes the custom event.
        /// </summary>
        public void Execute()
        {
            customEvent?.Invoke();
        }
    }
}
