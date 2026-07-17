using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Forge.Content;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace Serhat.Forge.Startup
{
    /// <summary>
    /// Boot state for the game system.
    /// </summary>
    public enum GameBootState
    {
        NotStarted,
        Initializing,
        CheckingCatalog,
        Preloading,
        RunningStartupSteps,
        Ready,
        Failed
    }

    /// <summary>
    /// Persistent application boot orchestrator. Dependencies are supplied by Zenject.
    /// </summary>
    public sealed class GameBootstrapper : MonoBehaviour, IGameBootstrapper
    {

        [Tooltip("Ordered generic startup steps (auth, save, remote config, analytics, etc.).")]
        [SerializeField] private StartupStep[] _startupSteps = Array.Empty<StartupStep>();

        [Header("Bootstrap Prefabs")]
        [SerializeField] private string[] _bootstrapPrefabKeys = Array.Empty<string>();
        [SerializeField] private bool _allowBootWithoutBootstrapPrefabs;

        [Header("Behavior")]
        [SerializeField] private bool _initializeOnStart = true;

        [Header("Scene Loading")]
        [Tooltip("Scene to load after boot completes successfully.")]
        [SerializeField] private string _nextSceneName = string.Empty;
        [Tooltip("Minimum time to show splash screen (seconds).")]
        [SerializeField] private float _minimumSplashTime = 1f;
        [Tooltip("Load scene automatically when boot completes.")]
        [SerializeField] private bool _autoLoadNextScene;

        private GameBootState _state = GameBootState.NotStarted;
        private string _errorMessage;
        private CancellationTokenSource _bootCts;
        private Task<bool> _activeBootTask;
        private Task _activeSceneLoadTask;
        private Task<bool> _restartTask;
        private float _bootStartTime;
        private static readonly TimeSpan RestartCancellationTimeout = TimeSpan.FromSeconds(5);

        private ContentConfiguration _contentConfiguration;
        private IContentManager _contentManager;
        private IPrefabLoader _prefabLoader;
        private RetryPolicy _retryPolicy;
        private StartupPipeline _startupPipeline;
        private bool _dependenciesInjected;
        /// <summary>
        /// Current boot state.
        /// </summary>
        public GameBootState State => _state;

        /// <summary>
        /// Whether boot has completed (successfully or with failure).
        /// </summary>
        public bool IsDone => _state == GameBootState.Ready || _state == GameBootState.Failed;

        /// <summary>
        /// Whether the system is ready to use.
        /// </summary>
        public bool IsReady => _state == GameBootState.Ready;

        /// <summary>
        /// Error message if boot failed.
        /// </summary>
        public string ErrorMessage => _errorMessage;

        /// <summary>
        /// The configuration being used.
        /// </summary>
        public ContentConfiguration Configuration => _contentConfiguration;

        /// <summary>
        /// Event raised when boot state changes.
        /// </summary>
        public event Action<GameBootState> OnStateChanged;

        /// <summary>
        /// Event raised when bootstrapping completes.
        /// </summary>
        public event Action<bool, string> OnBootComplete;

        /// <summary>
        /// Event raised for download progress during boot.
        /// </summary>
        public event DownloadProgressHandler OnDownloadProgress;
        public event Action<int, int, StartupStep> OnStartupStepStarted;

        [Inject]
        private void Construct(
            ContentConfiguration contentConfiguration,
            IContentManager contentManager,
            IPrefabLoader prefabLoader,
            RetryPolicy retryPolicy,
            StartupPipeline startupPipeline)
        {
            _contentConfiguration = contentConfiguration ??
                throw new ArgumentNullException(nameof(contentConfiguration));
            _contentManager = contentManager ??
                throw new ArgumentNullException(nameof(contentManager));
            _prefabLoader = prefabLoader ??
                throw new ArgumentNullException(nameof(prefabLoader));
            _retryPolicy = retryPolicy ??
                throw new ArgumentNullException(nameof(retryPolicy));
            _startupPipeline = startupPipeline ??
                throw new ArgumentNullException(nameof(startupPipeline));
            _dependenciesInjected = true;
        }

        private void Start()
        {
            if (_initializeOnStart)
            {
                _ = RunAutomaticBootAsync();
            }
        }

        private async Task RunAutomaticBootAsync()
        {
            try
            {
                await BootAndLoadSceneAsync();
            }
            catch (OperationCanceledException) when (_bootCts?.IsCancellationRequested == true)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void OnDestroy()
        {
            _bootCts?.Cancel();
            _bootCts?.Dispose();
            _bootCts = null;
        }

        /// <summary>
        /// Starts the boot process and loads the next scene when complete.
        /// </summary>
        public async Task<bool> BootAndLoadSceneAsync()
        {
            var bootTask = BootAsync();
            var success = await bootTask;

            if (!success || !ReferenceEquals(bootTask, _activeBootTask))
                return false;

            if (_autoLoadNextScene && !string.IsNullOrEmpty(_nextSceneName))
            {
                var cancellationToken = _bootCts?.Token ?? CancellationToken.None;
                try
                {
                    await LoadNextSceneAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Starts the boot process if not already started.
        /// </summary>
        public Task<bool> BootAsync()
        {
            if (_activeBootTask != null && !_activeBootTask.IsCompleted)
                return _activeBootTask;

            if (_state == GameBootState.Ready)
            {
                _activeBootTask ??= Task.FromResult(true);
                return _activeBootTask;
            }

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _activeBootTask = completion.Task;
            _ = RunBootCoreAsync(completion);
            return _activeBootTask;
        }

        private async Task RunBootCoreAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                completion.TrySetResult(await BootCoreAsync());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private async Task<bool> BootCoreAsync()
        {
            if (_state != GameBootState.NotStarted && _state != GameBootState.Failed)
            {
                if (_state == GameBootState.Ready)
                {
                    return true;
                }

                // Wait for ongoing boot
                while (!IsDone)
                {
                    await Task.Yield();
                }

                return _state == GameBootState.Ready;
            }

            _bootStartTime = Time.realtimeSinceStartup;
            _errorMessage = null;
            _bootCts?.Cancel();
            _bootCts?.Dispose();
            _bootCts = new CancellationTokenSource();

            var ct = _bootCts.Token;

            try
            {
                return await BootInternalAsync(ct);
            }
            catch (OperationCanceledException)
            {
                SetState(GameBootState.Failed);
                _errorMessage = "Boot cancelled.";
                NotifyBootComplete(false, _errorMessage);
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameBootstrapper] Boot exception: {ex.Message}");
                SetState(GameBootState.Failed);
                _errorMessage = ex.Message;
                NotifyBootComplete(false, _errorMessage);
                return false;
            }
        }

        private async Task<bool> BootInternalAsync(CancellationToken ct)
        {
            Log("Starting game initialization...");

            if (!_dependenciesInjected)
            {
                throw new InvalidOperationException(
                    "GameBootstrapper must be created by the Zenject composition root.");
            }

            var contentManager = _contentManager;
            contentManager.OnDownloadProgress += HandleDownloadProgress;

            try
            {
                // Step 3: Initialize Addressables
                SetState(GameBootState.Initializing);
                Log("Initializing Addressables...");

                var initResult = await contentManager.InitializeAsync(ct);
                if (initResult.IsFailure)
                {
                    _errorMessage = initResult.ErrorMessage;
                    SetState(GameBootState.Failed);
                    NotifyBootComplete(false, _errorMessage);
                    return false;
                }

                // Step 4: Check for catalog updates (if enabled)
                if (_contentConfiguration.CheckForCatalogUpdates)
                {
                    var catalogSuccess = await CheckCatalogUpdatesAsync(contentManager, ct);
                    if (!catalogSuccess && !_contentConfiguration.AllowOfflineMode)
                    {
                        NotifyBootComplete(false, _errorMessage);
                        return false;
                    }
                }

                // Step 5: Preload required content
                if (_contentConfiguration.BootPreloadLabels != null && _contentConfiguration.BootPreloadLabels.Count > 0)
                {
                    var preloadSuccess = await PreloadContentAsync(contentManager, ct);
                    if (!preloadSuccess)
                    {
                        if (!_contentConfiguration.AllowBootWithoutPreload)
                        {
                            SetState(GameBootState.Failed);
                            NotifyBootComplete(false, _errorMessage);
                            return false;
                        }

                        Debug.LogWarning($"[GameBootstrapper] {_errorMessage} Continuing because preload is optional.");
                        _errorMessage = null;
                    }
                }


                // Step 6: Load configured prefabs and retain their handles.
                var bootstrapPrefabsLoaded = await PreloadBootstrapPrefabsAsync(ct);
                if (!bootstrapPrefabsLoaded)
                {
                    if (!_allowBootWithoutBootstrapPrefabs)
                    {
                        SetState(GameBootState.Failed);
                        NotifyBootComplete(false, _errorMessage);
                        return false;
                    }

                    Debug.LogWarning($"[GameBootstrapper] {_errorMessage} Continuing because bootstrap prefabs are optional.");
                    _errorMessage = null;
                }
                // Step 7: Run project-defined infrastructure steps.
                if (!await RunStartupStepsAsync(ct))
                {
                    NotifyBootComplete(false, _errorMessage);
                    return false;
                }
                // Boot complete
                SetState(GameBootState.Ready);
                Log("Game initialization complete.");
                NotifyBootComplete(true, null);
                return true;
            }
            finally
            {
                contentManager.OnDownloadProgress -= HandleDownloadProgress;
            }
        }

        private async Task<bool> PreloadBootstrapPrefabsAsync(CancellationToken cancellationToken)
        {
            if (_bootstrapPrefabKeys == null || _bootstrapPrefabKeys.Length == 0)
                return true;

            try
            {
                var loader = _prefabLoader;
                var succeeded = await loader.PreloadAsync(_bootstrapPrefabKeys, cancellationToken);
                if (succeeded)
                    return true;

                _errorMessage = "One or more bootstrap prefabs failed to load.";
                return false;
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                _errorMessage = $"Bootstrap prefab loading failed: {exception.Message}";
                Debug.LogError($"[GameBootstrapper] {_errorMessage}");
                return false;
            }
        }
        private async Task<bool> RunStartupStepsAsync(CancellationToken cancellationToken)
        {

            if (_startupSteps == null || _startupSteps.Length == 0)
                return true;

            SetState(GameBootState.RunningStartupSteps);
            var pipeline = _startupPipeline;
            pipeline.StepStarted += HandleStartupStepStarted;

            try
            {
                var result = await pipeline.RunAsync(_startupSteps, cancellationToken);
                if (result.Succeeded)
                    return true;

                var failedStepName = result.FailedStep != null
                    ? result.FailedStep.StepName
                    : "configuration";
                _errorMessage = $"Startup step '{failedStepName}' failed: {result.Error.Message}";
                SetState(GameBootState.Failed);
                Debug.LogError($"[GameBootstrapper] {_errorMessage}", result.FailedStep);
                return false;
            }
            finally
            {
                pipeline.StepStarted -= HandleStartupStepStarted;
            }
        }

        private void HandleStartupStepStarted(int index, int count, StartupStep step)
        {
            OnStartupStepStarted?.Invoke(index, count, step);
        }

        /// <summary>
        /// Checks for catalog updates with retry logic.
        /// </summary>
        private async Task<bool> CheckCatalogUpdatesAsync(IContentManager contentManager, CancellationToken ct)
        {
            SetState(GameBootState.CheckingCatalog);
            Log("Checking for catalog updates...");

            if (!contentManager.IsNetworkAvailable())
            {
                if (_contentConfiguration.AllowOfflineMode)
                {
                    Debug.LogWarning("[GameBootstrapper] No network available, continuing with cached content.");
                    return true;
                }

                _errorMessage = "No network connection available.";
                SetState(GameBootState.Failed);
                return false;
            }

            // SRP: Use retry policy for catalog check
            var catalogResult = await _retryPolicy.ExecuteAsync(
                token => contentManager.CheckAndUpdateCatalogsAsync(token),
                ct);

            if (catalogResult.IsFailure)
            {
                if (_contentConfiguration.AllowOfflineMode)
                {
                    Debug.LogWarning($"[GameBootstrapper] Catalog update failed, continuing with cached content: {catalogResult.ErrorMessage}");
                    return true;
                }

                _errorMessage = catalogResult.ErrorMessage;
                SetState(GameBootState.Failed);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Preloads required content.
        /// </summary>
        private async Task<bool> PreloadContentAsync(IContentManager contentManager, CancellationToken ct)
        {
            SetState(GameBootState.Preloading);
            Log($"Preloading {_contentConfiguration.BootPreloadLabels.Count} label(s)...");

            var preloadSuccess = await contentManager.EnsureContentAsync(_contentConfiguration.BootPreloadLabels, ct);

            if (!preloadSuccess)
            {
                _errorMessage = "Failed to preload required content.";
                return false;
            }

            return true;
        }

        private void HandleDownloadProgress(DownloadProgress progress) =>
            InvokeSafely(OnDownloadProgress, progress);

        private void SetState(GameBootState newState)
        {
            if (_state == newState)
                return;

            _state = newState;
            InvokeSafely(OnStateChanged, newState);
        }

        private void NotifyBootComplete(bool succeeded, string error) =>
            InvokeSafely(OnBootComplete, succeeded, error);

        private void InvokeSafely<T>(Action<T> handlers, T value)
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
                    Debug.LogException(exception, this);
                }
            }
        }

        private void InvokeSafely(
            DownloadProgressHandler handlers,
            DownloadProgress progress)
        {
            if (handlers == null)
                return;

            foreach (DownloadProgressHandler handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(progress);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        private void InvokeSafely<TFirst, TSecond>(
            Action<TFirst, TSecond> handlers,
            TFirst first,
            TSecond second)
        {
            if (handlers == null)
                return;

            foreach (Action<TFirst, TSecond> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(first, second);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        private void Log(string message)
        {
            if (_contentConfiguration != null && _contentConfiguration.VerboseLogging)
            {
                Debug.Log($"[GameBootstrapper] {message}");
            }
        }

        /// <summary>
        /// Cancels the current boot operation.
        /// </summary>
        public void CancelBoot()
        {
            _bootCts?.Cancel();
        }

        /// <summary>
        /// Restarts the boot process.
        /// </summary>
        public Task<bool> RestartBootAsync()
        {
            if (_restartTask != null && !_restartTask.IsCompleted)
                return _restartTask;

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _restartTask = completion.Task;
            _ = RunRestartCoreAsync(completion);
            return _restartTask;
        }

        private async Task RunRestartCoreAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                completion.TrySetResult(await RestartBootCoreAsync());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private async Task<bool> RestartBootCoreAsync()
        {
            if (_activeSceneLoadTask != null && !_activeSceneLoadTask.IsCompleted)
            {
                _errorMessage =
                    "Boot restart aborted because a non-cancellable scene load is in progress.";
                Debug.LogError($"[GameBootstrapper] {_errorMessage}", this);
                return false;
            }

            var activeBoot = _activeBootTask;
            CancelBoot();

            if (activeBoot != null && !activeBoot.IsCompleted)
            {
                var completed = await Task.WhenAny(
                    activeBoot,
                    Task.Delay(RestartCancellationTimeout));
                if (completed != activeBoot)
                {
                    _errorMessage =
                        "Boot restart aborted because the active boot did not stop within " +
                        $"{RestartCancellationTimeout.TotalSeconds:0.#} seconds.";
                    Debug.LogError($"[GameBootstrapper] {_errorMessage}", this);
                    return false;
                }
            }

            if (activeBoot != null)
            {
                try
                {
                    await activeBoot;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            _prefabLoader.ReleaseAll();
            _state = GameBootState.NotStarted;
            _errorMessage = null;
            _activeBootTask = null;

            return await BootAsync();
        }

        /// <summary>
        /// Loads the next scene after ensuring minimum splash time has passed.
        /// </summary>
        public Task LoadNextSceneAsync() =>
            LoadNextSceneAsync(CancellationToken.None);

        private async Task LoadNextSceneAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(_nextSceneName))
            {
                Debug.LogWarning("[GameBootstrapper] No next scene specified.");
                return;
            }

            var elapsed = Time.realtimeSinceStartup - _bootStartTime;
            if (elapsed < _minimumSplashTime)
            {
                var remaining = _minimumSplashTime - elapsed;
                await Task.Delay(TimeSpan.FromSeconds(remaining), cancellationToken);
            }

            Log($"Loading scene: {_nextSceneName}");
            await LoadSceneOperationAsync(_nextSceneName, cancellationToken);
        }

        /// <summary>
        /// Loads a specific scene after boot completes.
        /// </summary>
        public async Task LoadSceneAsync(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogWarning("[GameBootstrapper] Scene name is empty.");
                return;
            }

            if (_state == GameBootState.NotStarted)
            {
                if (!await BootAsync())
                    return;
            }
            else if (!IsDone)
            {
                var activeBoot = _activeBootTask;
                if (activeBoot == null)
                {
                    throw new InvalidOperationException(
                        "Boot is in progress but no active boot task is available.");
                }

                if (!await activeBoot)
                    return;
            }

            if (!IsReady)
            {
                Debug.LogError($"[GameBootstrapper] Cannot load scene, boot failed: {_errorMessage}");
                return;
            }

            var elapsed = Time.realtimeSinceStartup - _bootStartTime;
            if (elapsed < _minimumSplashTime)
            {
                var remaining = _minimumSplashTime - elapsed;
                await Task.Delay(TimeSpan.FromSeconds(remaining));
            }

            Log($"Loading scene: {sceneName}");
            await LoadSceneOperationAsync(sceneName);
        }

        private Task LoadSceneOperationAsync(
            string sceneName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_activeSceneLoadTask != null && !_activeSceneLoadTask.IsCompleted)
            {
                throw new InvalidOperationException(
                    "A non-cancellable scene load is already in progress.");
            }

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _activeSceneLoadTask = completion.Task;
            _ = RunSceneLoadAsync(completion, sceneName, cancellationToken);
            return _activeSceneLoadTask;
        }

        private static async Task RunSceneLoadAsync(
            TaskCompletionSource<bool> completion,
            string sceneName,
            CancellationToken cancellationToken)
        {
            try
            {
                await LoadSceneOperationCoreAsync(sceneName, cancellationToken);
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private static async Task LoadSceneOperationCoreAsync(
            string sceneName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
                throw new InvalidOperationException($"Unity could not start loading scene '{sceneName}'.");

            // Unity scene loading cannot be cancelled once started. Keep tracking the
            // operation so restart/concurrent-load requests can fail deterministically.
            while (!operation.isDone)
            {
                await Task.Yield();
            }
        }

    }
}
