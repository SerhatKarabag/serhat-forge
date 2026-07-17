using System;
using System.Threading.Tasks;

namespace ScenarioSystem
{
    /// <summary>
    /// Base class for all scenario commands.
    /// Commands are individual actions that can be executed as part of a scenario.
    /// </summary>
    [Serializable]
    public abstract class BaseCommand
    {
        /// <summary>
        /// Whether this command is currently executing.
        /// </summary>
        [field: UnityEngine.SerializeField]
        public bool ExecutingInProgress { get; set; }

        /// <summary>
        /// If true, this command will be skipped during execution.
        /// </summary>
        [field: UnityEngine.SerializeField]
        public bool Muted { get; set; }

        /// <summary>
        /// If true, the scenario will wait for this command to complete before executing the next one.
        /// If false, the next command will start immediately (fire and forget).
        /// </summary>
        [field: UnityEngine.SerializeField]
        public bool WaitForComplete { get; set; } = true;

        /// <summary>
        /// If true, this command requires dependency injection.
        /// Override this in derived classes that need injected dependencies.
        /// </summary>
        public virtual bool InjectCommand => false;

        /// <summary>
        /// Executes the command asynchronously.
        /// </summary>
        public abstract Task Execute();

        /// <summary>
        /// Called when the command should save its Unity Object references.
        /// </summary>
        public virtual void OnSave(IUnityObjectSaver saver)
        {
        }

        /// <summary>
        /// Called when the command should restore its Unity Object references.
        /// </summary>
        public virtual void OnRestore(IUnityObjectSaver saver)
        {
        }

        /// <summary>
        /// Called when the command should release its stored references.
        /// </summary>
        public virtual void ReleaseResources(IUnityObjectSaver saver)
        {
        }

        /// <summary>
        /// Called to stop the command execution.
        /// </summary>
        public virtual void Stop()
        {
        }

        /// <summary>
        /// Called to dispose of any resources.
        /// </summary>
        public virtual void Dispose()
        {
        }

        /// <summary>
        /// Called to initialize the command.
        /// </summary>
        public virtual void Init()
        {
        }
    }

    /// <summary>
    /// Generic base command that accepts a parameter of type T.
    /// Useful for commands that need runtime parameters (e.g., target transform).
    /// </summary>
    [Serializable]
    public abstract class BaseCommand<T> : BaseCommand
    {
        /// <summary>
        /// Sets a runtime parameter for this command.
        /// </summary>
        public abstract void SetParameter(T parameter);
    }
}
