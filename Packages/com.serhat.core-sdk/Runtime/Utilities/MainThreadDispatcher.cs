using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Serhat.Core.Utilities
{
    /// <summary>
    /// Dispatches actions to the main Unity thread.
    /// Thread-safe singleton that can be used from any SDK or game code.
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private static readonly object _lock = new object();
        private static readonly Queue<Action> _actionQueue = new Queue<Action>();
        private static bool _isQuitting;
        private static int _mainThreadId;

        /// <summary>
        /// Gets or creates the dispatcher instance.
        /// </summary>
        public static MainThreadDispatcher Instance
        {
            get
            {
                if (_isQuitting)
                    return null;

                lock (_lock)
                {
                    if (_instance == null)
                    {
                        var go = new GameObject("[MainThreadDispatcher]");
                        _instance = go.AddComponent<MainThreadDispatcher>();
                        DontDestroyOnLoad(go);
                    }
                    return _instance;
                }
            }
        }

        /// <summary>
        /// Enqueues an action to be executed on the main thread.
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null)
                return;

            // If we're already on the main thread and no pending actions, execute immediately
            if (IsMainThread && _actionQueue.Count == 0)
            {
                action();
                return;
            }

            lock (_actionQueue)
            {
                _actionQueue.Enqueue(action);
            }

            // Ensure instance exists to process queue
            _ = Instance;
        }

        private static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CaptureMainThreadId()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            ProcessQueue();
        }

        private void ProcessQueue()
        {
            lock (_actionQueue)
            {
                while (_actionQueue.Count > 0)
                {
                    var action = _actionQueue.Dequeue();
                    try
                    {
                        action?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
