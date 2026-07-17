using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that executes an async operation and waits for a callback.
    /// Useful for custom async operations that need to signal completion.
    /// </summary>
    [Serializable]
    public class AsyncCommand : BaseCommand
    {
        [SerializeField]
        private UnityEvent _onExecute = new UnityEvent();

        [SerializeField]
        private float _timeout = 10f;

        [SerializeField]
        private bool _useTimeout = true;

        private bool _isCancelled;
        private bool _isComplete;

        public UnityEvent OnExecuteEvent => _onExecute;

        public float Timeout
        {
            get => _timeout;
            set => _timeout = value;
        }

        public bool UseTimeout
        {
            get => _useTimeout;
            set => _useTimeout = value;
        }

        public AsyncCommand()
        {
        }

        public AsyncCommand(float timeout)
        {
            _timeout = timeout;
        }

        public override async Task Execute()
        {
            _isCancelled = false;
            _isComplete = false;

            // Invoke the event
            _onExecute?.Invoke();

            // Wait for completion or timeout
            float elapsed = 0f;

            while (!_isComplete && !_isCancelled)
            {
                await Task.Yield();
                elapsed += Time.deltaTime;

                if (_useTimeout && elapsed >= _timeout)
                {
                    Debug.LogWarning($"[AsyncCommand] Timed out after {_timeout} seconds.");
                    break;
                }
            }
        }

        /// <summary>
        /// Call this method to signal that the async operation is complete.
        /// Can be called from UnityEvent or from code.
        /// </summary>
        public void Complete()
        {
            _isComplete = true;
        }

        /// <summary>
        /// Static method that can be used with UnityEvent to complete a specific command.
        /// </summary>
        public static void CompleteCommand(AsyncCommand command)
        {
            command?.Complete();
        }

        public override void Stop()
        {
            _isCancelled = true;
        }
    }

    /// <summary>
    /// Command that waits for a condition to be true.
    /// Uses a callback to check the condition each frame.
    /// </summary>
    [Serializable]
    public class WaitForConditionCommand : BaseCommand
    {
        [SerializeField]
        private float _timeout = 10f;

        [SerializeField]
        private bool _useTimeout = true;

        [SerializeField]
        private float _checkInterval = 0f;

        private bool _isCancelled;
        private Func<bool> _condition;

        public float Timeout
        {
            get => _timeout;
            set => _timeout = value;
        }

        public float CheckInterval
        {
            get => _checkInterval;
            set => _checkInterval = value;
        }

        public WaitForConditionCommand()
        {
        }

        public WaitForConditionCommand(Func<bool> condition, float timeout = 10f)
        {
            _condition = condition;
            _timeout = timeout;
        }

        /// <summary>
        /// Sets the condition to wait for.
        /// </summary>
        public void SetCondition(Func<bool> condition)
        {
            _condition = condition;
        }

        public override async Task Execute()
        {
            _isCancelled = false;

            if (_condition == null)
            {
                Debug.LogWarning("[WaitForConditionCommand] No condition set.");
                return;
            }

            float elapsed = 0f;
            float lastCheck = 0f;

            while (!_isCancelled)
            {
                // Check condition
                if (_checkInterval <= 0 || elapsed - lastCheck >= _checkInterval)
                {
                    lastCheck = elapsed;

                    try
                    {
                        if (_condition.Invoke())
                        {
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[WaitForConditionCommand] Error checking condition: {e}");
                        break;
                    }
                }

                await Task.Yield();
                elapsed += Time.deltaTime;

                if (_useTimeout && elapsed >= _timeout)
                {
                    Debug.LogWarning($"[WaitForConditionCommand] Timed out after {_timeout} seconds.");
                    break;
                }
            }
        }

        public override void Stop()
        {
            _isCancelled = true;
        }
    }

    /// <summary>
    /// Command that waits for a specific frame count.
    /// </summary>
    [Serializable]
    public class WaitForFramesCommand : BaseCommand
    {
        [SerializeField]
        private int _frameCount = 1;

        private bool _isCancelled;

        public int FrameCount
        {
            get => _frameCount;
            set => _frameCount = Mathf.Max(1, value);
        }

        public WaitForFramesCommand()
        {
        }

        public WaitForFramesCommand(int frameCount)
        {
            _frameCount = Mathf.Max(1, frameCount);
        }

        public override async Task Execute()
        {
            _isCancelled = false;

            for (int i = 0; i < _frameCount && !_isCancelled; i++)
            {
                await Task.Yield();
            }
        }

        public override void Stop()
        {
            _isCancelled = true;
        }
    }

    /// <summary>
    /// Command that waits until the end of the current frame.
    /// </summary>
    [Serializable]
    public class WaitForEndOfFrameCommand : BaseCommand
    {
        public override async Task Execute()
        {
            await Task.Yield();
        }
    }
}
