using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ScenarioSystem
{
    /// <summary>
    /// Main scenario implementation that executes a sequence of commands.
    /// Supports serialization, looping, and cancellation.
    /// </summary>
    public class Scenario : ScenarioBase, IUnityObjectSaver
    {
        [SerializeField]
        [Tooltip("List of saved Unity Object references used by commands")]
        protected List<SavedObjectEntry> savedObjects = new List<SavedObjectEntry>();

        [SerializeField]
        [Tooltip("Play the scenario automatically on Start")]
        private bool playOnStart;

        [SerializeField]
        [Tooltip("Stop the scenario automatically when disabled")]
        private bool stopOnDisable;

        [SerializeField]
        [Tooltip("Restore cancellation token before playing if it was cancelled")]
        private bool uncancelTokenOnPlay;

        [SerializeField]
        [Tooltip("Loop the scenario when all commands finish")]
        protected bool loopScenario;

        [SerializeField]
        [SerializeReference]
        [Tooltip("List of commands to execute in sequence")]
        private List<BaseCommand> _commands = new List<BaseCommand>();

        private CancellationTokenSource _cancelTokenSource;
        private CancellationToken _token;
        private bool _isInExecution;
        private object _parameter;

        public override bool IsInExecution
        {
            get => _isInExecution;
            protected set => _isInExecution = value;
        }

        public override List<BaseCommand> Commands => _commands;

        /// <summary>
        /// Event fired when scenario starts executing.
        /// </summary>
        public event Action OnScenarioStarted;

        /// <summary>
        /// Event fired when scenario finishes executing.
        /// </summary>
        public event Action OnScenarioCompleted;

        /// <summary>
        /// Event fired when scenario is cancelled.
        /// </summary>
        public event Action OnScenarioCancelled;

        protected virtual void Awake()
        {
            _cancelTokenSource = new CancellationTokenSource();
            _token = _cancelTokenSource.Token;

            // Initialize all commands
            foreach (var command in _commands)
            {
                command.Init();
                command.OnRestore(this);
            }
        }

        protected virtual void Start()
        {
            if (playOnStart)
            {
                Execute();
            }
        }

        protected virtual void OnDisable()
        {
            if (stopOnDisable && IsInExecution)
            {
                CancelExecution();
            }
        }

        protected virtual void OnDestroy()
        {
            foreach (var command in _commands)
            {
                command.Dispose();
            }
            _cancelTokenSource?.Dispose();
        }

        public override async void Execute()
        {
            if (uncancelTokenOnPlay && _cancelTokenSource.IsCancellationRequested)
            {
                RestoreCancelToken();
            }

            await ExecuteCommands();
        }

        public override void CancelExecution()
        {
            _cancelTokenSource?.Cancel();

            foreach (var command in _commands)
            {
                command.Stop();
            }

            IsInExecution = false;
            OnScenarioCancelled?.Invoke();
        }

        public override void RestoreCancelToken()
        {
            _cancelTokenSource?.Dispose();
            _cancelTokenSource = new CancellationTokenSource();
            _token = _cancelTokenSource.Token;
        }

        public override async Task ExecuteCommands()
        {
            if (IsInExecution)
            {
                Debug.LogWarning($"[Scenario] {name} is already executing.");
                return;
            }

            IsInExecution = true;
            OnScenarioStarted?.Invoke();

            try
            {
                if (loopScenario)
                {
                    await ExecuteCommandsInLoopInternal();
                }
                else
                {
                    await ExecuteCommandsInternal();
                }
            }
            catch (OperationCanceledException)
            {
                // Execution was cancelled
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scenario] Error executing scenario {name}: {e}");
            }
            finally
            {
                IsInExecution = false;
                OnScenarioCompleted?.Invoke();
            }
        }

        public override void SetParameter<T>(T parameter)
        {
            _parameter = parameter;

            foreach (var command in _commands)
            {
                if (command is BaseCommand<T> typedCommand)
                {
                    typedCommand.SetParameter(parameter);
                }
            }
        }

        private async Task ExecuteCommandsInternal()
        {
            foreach (var command in _commands)
            {
                if (_token.IsCancellationRequested)
                    break;

                if (command.Muted)
                    continue;

                await ExecuteCommand(command);
            }
        }

        private async Task ExecuteCommandsInLoopInternal()
        {
            while (!_token.IsCancellationRequested)
            {
                await ExecuteCommandsInternal();
            }
        }

        private async Task ExecuteCommand(BaseCommand command)
        {
            try
            {
                command.ExecutingInProgress = true;

                if (command.WaitForComplete)
                {
                    await command.Execute();
                }
                else
                {
                    // Fire and forget
                    _ = ExecuteCommandWithoutWaiting(command);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scenario] Error executing command {command.GetType().Name}: {e}");
            }
            finally
            {
                command.ExecutingInProgress = false;
            }
        }

        private async Task ExecuteCommandWithoutWaiting(BaseCommand command)
        {
            try
            {
                await command.Execute();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scenario] Error in fire-and-forget command {command.GetType().Name}: {e}");
            }
            finally
            {
                command.ExecutingInProgress = false;
            }
        }

        #region IUnityObjectSaver Implementation

        public void Save(string key, UnityEngine.Object savedObject)
        {
            var existing = savedObjects.Find(e => e.key == key);
            if (existing != null)
            {
                existing.savedObject = savedObject;
            }
            else
            {
                savedObjects.Add(new SavedObjectEntry(key, savedObject));
            }
        }

        public UnityEngine.Object Restore(string key)
        {
            var entry = savedObjects.Find(e => e.key == key);
            return entry?.savedObject;
        }

        public void RemoveStoredObjectByKey(string key)
        {
            savedObjects.RemoveAll(e => e.key == key);
        }

        #endregion

        #region Command Management

        /// <summary>
        /// Adds a command to the scenario.
        /// </summary>
        public void AddCommand(BaseCommand command)
        {
            _commands.Add(command);
            command.Init();
            command.OnRestore(this);
        }

        /// <summary>
        /// Removes a command at the specified index.
        /// </summary>
        public void RemoveCommandByIndex(int index)
        {
            if (index >= 0 && index < _commands.Count)
            {
                var command = _commands[index];
                command.ReleaseResources(this);
                command.Dispose();
                _commands.RemoveAt(index);
            }
        }

        /// <summary>
        /// Clears all commands from the scenario.
        /// </summary>
        public void ClearCommands()
        {
            foreach (var command in _commands)
            {
                command.ReleaseResources(this);
                command.Dispose();
            }
            _commands.Clear();
        }

        #endregion
    }
}
