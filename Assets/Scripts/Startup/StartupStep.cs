using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Forge.Startup
{
    /// <summary>
    /// Extensible scene-side startup operation. Implement one responsibility per step
    /// (authentication, remote config, analytics, save loading, force-update, etc.).
    /// </summary>
    public abstract class StartupStep : MonoBehaviour
    {
        [SerializeField] private string _displayName;
        [SerializeField] private bool _required = true;
        [SerializeField, Min(0f)] private float _timeoutSeconds = 30f;
        [SerializeField, Min(0f)] private float _cancellationGraceSeconds = 1f;
        [SerializeField, Min(0)] private int _retryCount;
        [SerializeField, Min(0f)] private float _retryDelaySeconds = 0.5f;

        public string StepName => string.IsNullOrWhiteSpace(_displayName) ? GetType().Name : _displayName;
        public bool IsRequired => _required;
        public int RetryCount => _retryCount;
        public float RetryDelaySeconds => _retryDelaySeconds;
        public float TimeoutSeconds => _timeoutSeconds;
        public float CancellationGraceSeconds => _cancellationGraceSeconds;

        /// <summary>
        /// Executes this step. Implementations must honor the cancellation token.
        /// Throw on failure; optional/required behavior is handled by the pipeline.
        /// </summary>
        public abstract Task ExecuteAsync(CancellationToken cancellationToken);

#if UNITY_EDITOR
        private void OnValidate()
        {
            _timeoutSeconds = Mathf.Max(0f, _timeoutSeconds);
            _cancellationGraceSeconds = Mathf.Max(0f, _cancellationGraceSeconds);
            _retryCount = Mathf.Max(0, _retryCount);
            _retryDelaySeconds = Mathf.Max(0f, _retryDelaySeconds);
        }
#endif
    }
}
