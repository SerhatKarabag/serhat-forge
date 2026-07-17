#if PLAYFAB_SDK && SERHAT_FORGE_GAME_API_SAMPLE
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.GameApi;
using Serhat.Backend.PlayFab;
using UnityEngine;

namespace Serhat.Forge.Backend
{
    /// <summary>
    /// Scene-side backend bootstrapper that wires the <see cref="GameApiClient"/> with a
    /// PlayFab CloudScript transport. Adapt this for your own backend if you don't use PlayFab.
    ///
    /// <para>This is a STARTING POINT — replace title-id, swap the invoker, or change the
    /// telemetry sink as your project requires.</para>
    ///
    /// <para>Usage:</para>
    /// <code>
    /// async Task LoadBackendAsync(CancellationToken cancellationToken)
    /// {
    ///     var manager = BackendManager.Instance
    ///         ?? throw new InvalidOperationException("BackendManager is not present in the scene.");
    ///
    ///     var client = await manager.InitializeAsync(cancellationToken);
    ///     var result = await client.GetBootstrapAsync(cancellationToken);
    ///     result.Match(
    ///         bootstrap => Debug.Log($"Player: {bootstrap.Progress.PlayerId}"),
    ///         error => Debug.LogError(error));
    /// }
    /// </code>
    /// </summary>
    public sealed class BackendManager : MonoBehaviour
    {
        public static BackendManager? Instance { get; private set; }

        [Header("PlayFab Cloud Script")]
        [Tooltip("PlayFab Title Id. Override at runtime via SetTitleId(...) for environment switches.")]
        [SerializeField] private string _titleId = "";

        [Tooltip("Logical environment label (dev/staging/prod). Forwarded to telemetry.")]
        [SerializeField] private string _environment = "dev";

        [Header("Resilience")]
        [Tooltip("Per-request timeout in seconds.")]
        [SerializeField] private int _requestTimeoutSeconds = 15;

        [Tooltip("Max retry attempts on transient errors.")]
        [SerializeField] private int _maxRetries = 2;

        public IGameApiClient? Client { get; private set; }
        public bool IsReady => Client != null;

        public event Action<IGameApiClient>? OnReady;
        public event Action<Exception>? OnInitializeFailed;

        private CancellationTokenSource? _initCts;
        private bool _initializing;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            _initCts?.Cancel();
            _initCts?.Dispose();
            Client?.Dispose();
            Client = null;
        }

        public void SetTitleId(string titleId) => _titleId = titleId ?? string.Empty;
        public void SetEnvironment(string env) => _environment = env ?? "dev";

        /// <summary>
        /// Initializes the GameApiClient using PlayFab CloudScript as transport.
        /// Throws if the title id is missing.
        /// </summary>
        public async Task<IGameApiClient> InitializeAsync(CancellationToken ct = default)
        {
            if (Client != null)
                return Client;

            if (_initializing)
                throw new InvalidOperationException("[BackendManager] Already initializing.");

            if (string.IsNullOrEmpty(_titleId))
                throw new InvalidOperationException("[BackendManager] Title id is empty. Set it via Inspector or SetTitleId(...) before InitializeAsync.");

            _initializing = true;
            _initCts?.Dispose();
            _initCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                var serializer = new PlayFabSimpleJsonSerializer();
                var clock = SystemClock.Instance;
                var logger = new UnityBackendLogger("BackendManager");

                var invokerOptions = new BackendSdkOptions
                {
                    TitleId = _titleId,
                    Environment = _environment,
                    DefaultTimeout = TimeSpan.FromSeconds(_requestTimeoutSeconds),
                };
                invokerOptions.Retry.MaxAttempts = _maxRetries;

                var invoker = new PlayFabCloudFunctionInvoker(invokerOptions, serializer, logger, clock);

                var client = await GameApiClientBuilder.Create()
                    .WithTitleId(_titleId)
                    .WithEnvironment(_environment)
                    .WithSerializer(serializer)
                    .WithClock(clock)
                    .WithInvoker(invoker)
                    .WithOptions(o =>
                    {
                        o.DefaultTimeout = TimeSpan.FromSeconds(_requestTimeoutSeconds);
                        o.Retry.MaxAttempts = _maxRetries;
                    })
                    .BuildAsync(_initCts.Token);

                _initCts.Token.ThrowIfCancellationRequested();
                Client = client;
                InvokeSafely(OnReady, client);
                Debug.Log($"[BackendManager] Ready (title={_titleId}, env={_environment})");
                return client;
            }
            catch (OperationCanceledException) when (_initCts.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BackendManager] InitializeAsync failed: {ex.Message}");
                InvokeSafely(OnInitializeFailed, ex);
                throw;
            }
            finally
            {
                _initializing = false;
            }
        }

        private static void InvokeSafely<T>(Action<T>? handlers, T value)
        {
            if (handlers == null)
                return;

            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(value);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
#endif
