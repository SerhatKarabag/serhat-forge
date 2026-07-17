using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem
{
    /// <summary>
    /// Abstract base class for all scenarios.
    /// A scenario is a sequence of commands that execute in order.
    /// </summary>
    public abstract class ScenarioBase : MonoBehaviour
    {
        /// <summary>
        /// Whether the scenario is currently executing.
        /// </summary>
        public abstract bool IsInExecution { get; protected set; }

        /// <summary>
        /// List of commands in this scenario.
        /// </summary>
        public abstract List<BaseCommand> Commands { get; }

        /// <summary>
        /// Starts executing the scenario.
        /// </summary>
        public abstract void Execute();

        /// <summary>
        /// Executes all commands and returns a Task that completes when done.
        /// </summary>
        public abstract Task ExecuteCommands();

        /// <summary>
        /// Sets a parameter that will be passed to commands that support it.
        /// </summary>
        public abstract void SetParameter<T>(T parameter);

        /// <summary>
        /// Cancels the current execution.
        /// </summary>
        public abstract void CancelExecution();

        /// <summary>
        /// Restores the cancellation token for reuse.
        /// </summary>
        public abstract void RestoreCancelToken();
    }
}
