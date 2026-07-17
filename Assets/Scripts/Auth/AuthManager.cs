using System;
using System.Threading;
using System.Threading.Tasks;
using PlayFab;
using UnityEngine;

namespace Serhat.Forge.Auth
{
    public class AuthManager : MonoBehaviour
    {
        // Events
        public event Action<AuthState, AuthState> OnAuthStateChanged;
        public event Action<string> OnUserMessage;
        public event Action<AuthError> OnFatalError;
        public event Action<AuthSessionData> OnLoginSuccess;

        // State
        private AuthState _currentState = AuthState.Uninitialized;
        private AuthSessionData _session;
        private string _persistentCustomId;
        private const string LinkedProviderKey = "SerhatForge_Auth_LinkedProvider";
        private const string LinkedProviderGoogle = "google";
        private const string LinkedProviderGameCenter = "gamecenter";
        private const string GoogleStartupPromptShownKey = "SerhatForge_Auth_GoogleStartupPromptShown";
        private const float SecureStorageTimeoutSeconds = 5f;
        private const float AutomaticPlatformAuthTimeoutSeconds = 12f;
        private const float InteractivePlatformAuthTimeoutSeconds = 45f;
        private const float PlayFabRequestTimeoutSeconds = 15f;
        private const float UnlinkRequestTimeoutSeconds = 10f;
        private const float StuckOperationRecoverySeconds = 45f;

        private AuthOperation _activeOperation;
        private long _nextOperationId;
        private readonly object _stateLock = new object();

        // Dependencies
        private ISecureStorage _secureStorage;
        private IPlayFabAuthService _playFabService;
        private IGoogleAuthProvider _googleProvider;
        private IGameCenterAuthProvider _gameCenterProvider;

        // Logging
        private string _currentCorrelationId;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            InitializeDependencies();
        }

        private void InitializeDependencies()
        {
            // Ensure PlayFab Title ID is configured
            if (string.IsNullOrWhiteSpace(PlayFabSettings.TitleId))
            {
                if (string.IsNullOrWhiteSpace(AuthConfig.PlayFabTitleId))
                {
                    throw new InvalidOperationException(
                        "Auth is enabled, but no PlayFab Title ID is configured. " +
                        "Set AuthConfig.PlayFabTitleId or PlayFabSettings.TitleId before initialization.");
                }

                PlayFabSettings.TitleId = AuthConfig.PlayFabTitleId;
                Debug.Log($"[AuthManager] PlayFab Title ID configured: {PlayFabSettings.TitleId}");
            }

            _secureStorage = SecureStorageFactory.Create();
            _playFabService = new PlayFabAuthService();

#if UNITY_ANDROID && !UNITY_EDITOR
            _googleProvider = CreateGoogleAuthProvider();
#endif

#if UNITY_IOS && !UNITY_EDITOR
            _gameCenterProvider = new GameCenterAuthProvider();
#endif
        }

        private static IGoogleAuthProvider CreateGoogleAuthProvider()
        {
            const string providerTypeName =
                "Serhat.Forge.Auth.GoogleAuthProvider, Serhat.Forge.Auth.GooglePlayGames";
            var providerType = Type.GetType(providerTypeName, throwOnError: false);
            if (providerType == null)
            {
                Debug.LogWarning(
                    "[AuthManager] Google Play Games provider is not enabled; Android will use anonymous fallback.");
                return null;
            }

            try
            {
                return Activator.CreateInstance(providerType) as IGoogleAuthProvider;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AuthManager] Google Play Games provider could not be created: {exception.Message}");
                return null;
            }
        }

        // === PUBLIC API ===

        /// <summary>Initialize and perform automatic login on app launch</summary>
        /// <remarks>
        /// Android flow: Try Google Play Games first, fallback to anonymous if fails
        /// iOS flow: Try Game Center first, fallback to anonymous if fails
        /// Returning linked users never fall back to anonymous because that can create a fresh PlayFab account.
        /// </remarks>
        public async void InitializeAndLogin()
        {
            if (!TryStartOperation("InitAndLogin", out var operation))
            {
                LogWarning("InitializeAndLogin blocked — another operation is in progress");
                return;
            }

            try
            {
                SetState(AuthState.Initializing, operation);
                ThrowIfOperationInactive(operation);
                _currentCorrelationId = Guid.NewGuid().ToString("N").Substring(0, 8);
                LogInfo($"[{_currentCorrelationId}] ---- Auth flow starting ----");
                LogInfo($"[{_currentCorrelationId}] Platform: {Application.platform}");

                // Step 1: Check for existing linked provider (returning user)
                bool hasLinkedProvider = TryGetLinkedProvider(out var linkedProvider);
                LogInfo($"[{_currentCorrelationId}] Step 1: Checking for linked provider...");
                if (await TryLoginWithLinkedProviderAsync(operation))
                {
                    LogInfo($"[{_currentCorrelationId}] ---- Auth flow completed via linked provider ----");
                    return;
                }
                LogInfo(hasLinkedProvider
                    ? $"[{_currentCorrelationId}] Linked provider login unavailable, trying platform login..."
                    : $"[{_currentCorrelationId}] No linked provider found, trying platform login...");

#if UNITY_ANDROID && !UNITY_EDITOR
                // Step 2 (Android): Try Google Play Games
                LogInfo($"[{_currentCorrelationId}] Step 2: Attempting Google Play Games login...");
                if (await TryInitialGoogleLoginAsync(operation, isFreshInstallFlow: !hasLinkedProvider))
                {
                    LogInfo($"[{_currentCorrelationId}] ---- Auth flow completed via Google Play Games ----");
                    return;
                }
                LogInfo($"[{_currentCorrelationId}] Google Play Games unavailable, falling back to anonymous login");
#endif

#if UNITY_IOS && !UNITY_EDITOR
                // Step 2 (iOS): Try Game Center
                LogInfo($"[{_currentCorrelationId}] Step 2: Attempting Game Center login...");
                if (await TryInitialGameCenterLoginAsync(operation))
                {
                    LogInfo($"[{_currentCorrelationId}] ---- Auth flow completed via Game Center ----");
                    return;
                }
                LogInfo($"[{_currentCorrelationId}] Game Center unavailable, falling back to anonymous login");
#endif

                if (hasLinkedProvider)
                {
                    LogWarning(
                        $"[{_currentCorrelationId}] Linked provider '{linkedProvider}' could not be restored. " +
                        "Anonymous fallback blocked to avoid creating a fresh PlayFab account.");
                    HandleLinkedProviderRestoreFailure(linkedProvider, operation);
                    return;
                }

                // Step 3: Fallback to anonymous CustomID login
                LogInfo($"[{_currentCorrelationId}] Step 3: Anonymous CustomID login...");
                await LoginAnonWithPersistentCustomIdAsync(operation);
                LogInfo($"[{_currentCorrelationId}] ---- Auth flow completed via anonymous login ----");
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            {
                LogInfo(
                    $"Auth operation {operation.Id} ('{operation.Name}') was cancelled.");
            }
            catch (Exception ex)
            {
                LogError($"[{_currentCorrelationId}] Unexpected error in InitializeAndLogin: {ex}");
                HandleError(AuthError.Generic($"Unexpected error: {ex.Message}"), operation);
            }
            finally
            {
                CompleteOperation(operation);
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Try initial Google Play Games login (first launch on Android)</summary>
        /// <returns>True if login succeeded, false if should fallback to anonymous</returns>
        private async Task<bool> TryInitialGoogleLoginAsync(AuthOperation operation, bool isFreshInstallFlow)
        {
            if (_googleProvider == null)
            {
                LogWarning($"[{_currentCorrelationId}] Google provider not available");
                return false;
            }

            LogInfo($"[{_currentCorrelationId}] Attempting initial Google Play Games login");

            // Try silent authentication first (no UI popup)
            var tokenResult = await GetGoogleServerAuthCodeWithTimeoutAsync(
                operation,
                allowInteractive: false,
                operationLabel: "Google Play Games silent auth");
            if (!tokenResult.IsSuccess)
            {
                LogInfo($"[{_currentCorrelationId}] Google silent auth failed: {tokenResult.Error.DebugMessage}");

                if (!ShouldAttemptInteractiveGoogleStartup(operation, isFreshInstallFlow))
                {
                    return false;
                }

                LogInfo($"[{_currentCorrelationId}] Attempting interactive Google Play Games login...");
                tokenResult = await GetGoogleServerAuthCodeWithTimeoutAsync(
                    operation,
                    allowInteractive: true,
                    operationLabel: "Google Play Games interactive auth");
                if (!tokenResult.IsSuccess)
                {
                    LogInfo($"[{_currentCorrelationId}] Google interactive auth failed: {tokenResult.Error.DebugMessage}");
                    return false;
                }
            }

            return await CompleteInitialGoogleLoginAsync(operation, tokenResult.Value);
        }

        private async Task<bool> CompleteInitialGoogleLoginAsync(AuthOperation operation, string serverAuthCode)
        {
            var loginResult = await LoginWithGoogleWithTimeoutAsync(
                operation,
                serverAuthCode,
                createAccount: true,
                operationLabel: "PlayFab Google login");
            if (!loginResult.IsSuccess)
            {
                LogWarning($"[{_currentCorrelationId}] PlayFab Google login failed: {loginResult.Error.DebugMessage}");
                return false;
            }

            ThrowIfOperationInactive(operation);
            _session = loginResult.Value;
            SetLinkedProvider(LinkedProviderGoogle, operation);
            SetState(AuthState.LoggedInProvider, operation);
            ThrowIfOperationInactive(operation);
            OnLoginSuccess?.Invoke(_session);
            LogInfo($"[{_currentCorrelationId}] Initial Google login successful: {_session.PlayFabId}");
            CompleteOperation(operation);
            return true;
        }

        private bool ShouldAttemptInteractiveGoogleStartup(
            AuthOperation operation,
            bool isFreshInstallFlow)
        {
            ThrowIfOperationInactive(operation);
            if (!isFreshInstallFlow)
            {
                return true;
            }

            if (PlayerPrefs.GetInt(GoogleStartupPromptShownKey, 0) != 0)
            {
                LogInfo($"[{_currentCorrelationId}] Interactive Google startup prompt already shown previously, skipping");
                return false;
            }

            ThrowIfOperationInactive(operation);
            PlayerPrefs.SetInt(GoogleStartupPromptShownKey, 1);
            PlayerPrefs.Save();
            return true;
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        /// <summary>Try initial Game Center login (first launch on iOS)</summary>
        /// <returns>True if login succeeded, false if should fallback to anonymous</returns>
        private async Task<bool> TryInitialGameCenterLoginAsync(AuthOperation operation)
        {
            if (_gameCenterProvider == null)
            {
                LogWarning($"[{_currentCorrelationId}] Game Center provider not available");
                return false;
            }

            LogInfo($"[{_currentCorrelationId}] Attempting initial Game Center login");

            var credentialResult = await AuthenticateGameCenterWithTimeoutAsync(
                operation,
                allowInteractive: false,
                operationLabel: "Game Center startup auth");
            if (!credentialResult.IsSuccess)
            {
                LogInfo($"[{_currentCorrelationId}] Game Center auth failed: {credentialResult.Error.DebugMessage}");
                return false;
            }

            var loginResult = await LoginWithGameCenterWithTimeoutAsync(
                operation,
                credentialResult.Value,
                createAccount: true,
                operationLabel: "PlayFab Game Center login");
            if (!loginResult.IsSuccess)
            {
                LogWarning($"[{_currentCorrelationId}] PlayFab Game Center login failed: {loginResult.Error.DebugMessage}");
                return false;
            }

            ThrowIfOperationInactive(operation);
            _session = loginResult.Value;
            SetLinkedProvider(LinkedProviderGameCenter, operation);
            SetState(AuthState.LoggedInProvider, operation);
            ThrowIfOperationInactive(operation);
            OnLoginSuccess?.Invoke(_session);
            LogInfo($"[{_currentCorrelationId}] Initial Game Center login successful: {_session.PlayFabId}");
            CompleteOperation(operation);
            return true;
        }
#endif

        /// <summary>Link Game Center account and disable anonymous login</summary>
        public async void LinkGameCenterAndDisableAnon()
        {
            if (!TryStartOperation("LinkGameCenter", out var operation))
            {
                OnUserMessage?.Invoke("İşlem devam ediyor. Lütfen bekleyin.");
                return;
            }

#if !UNITY_IOS || UNITY_EDITOR
            OnUserMessage?.Invoke("Game Center girişi sadece iOS'ta desteklenmektedir.");
            CompleteOperation(operation);
            return;
#else
            try
            {
                SetState(AuthState.LinkingProvider, operation);
                ThrowIfOperationInactive(operation);
                _currentCorrelationId = Guid.NewGuid().ToString("N").Substring(0, 8);
                LogInfo($"[{_currentCorrelationId}] Starting Game Center link");

                var credentialResult = await AuthenticateGameCenterWithTimeoutAsync(
                    operation,
                    allowInteractive: true,
                    operationLabel: "Game Center link auth");
                if (!credentialResult.IsSuccess)
                {
                    HandleError(credentialResult.Error, operation);
                    return;
                }

                var linkResult = await LinkGameCenterWithTimeoutAsync(operation, credentialResult.Value);
                if (!linkResult.IsSuccess)
                {
                    HandleError(linkResult.Error, operation);
                    return;
                }

                SetLinkedProvider(LinkedProviderGameCenter, operation);
                LogInfo($"[{_currentCorrelationId}] Game Center link successful");

                await UnlinkAnonCustomIdAsync(operation);

                if (_currentState != AuthState.Error)
                {
                    ThrowIfOperationInactive(operation);
                    _session.HasProviderLink = true;
                    SetState(AuthState.LoggedInProvider, operation);
                    ThrowIfOperationInactive(operation);
                    OnUserMessage?.Invoke("Game Center hesabı başarıyla bağlandı.");
                    LogInfo($"[{_currentCorrelationId}] Game Center link flow completed");
                }

                CompleteOperation(operation);
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            {
                LogInfo($"Auth operation {operation.Id} ('{operation.Name}') was cancelled.");
            }

            catch (Exception ex)
            {
                LogError($"[{_currentCorrelationId}] Unexpected error in LinkGameCenterAndDisableAnon: {ex}");
                HandleError(AuthError.Generic($"Unexpected error: {ex.Message}"), operation);
            }
#endif
        }

        /// <summary>Load existing account via Game Center (post-reinstall recovery)</summary>
        public async void LoadAccountWithGameCenter()
        {
            if (!TryStartOperation("LoadGameCenter", out var operation))
            {
                OnUserMessage?.Invoke("İşlem devam ediyor. Lütfen bekleyin.");
                return;
            }

#if !UNITY_IOS || UNITY_EDITOR
            OnUserMessage?.Invoke("Game Center girişi sadece iOS'ta desteklenmektedir.");
            CompleteOperation(operation);
            return;
#else
            try
            {
                SetState(AuthState.LoggingInAnon, operation);
                ThrowIfOperationInactive(operation);
                _currentCorrelationId = Guid.NewGuid().ToString("N").Substring(0, 8);
                LogInfo($"[{_currentCorrelationId}] Loading account via Game Center");

                var credentialResult = await AuthenticateGameCenterWithTimeoutAsync(
                    operation,
                    allowInteractive: true,
                    operationLabel: "Game Center account restore auth");
                if (!credentialResult.IsSuccess)
                {
                    HandleError(credentialResult.Error, operation);
                    return;
                }

                var loginResult = await LoginWithGameCenterWithTimeoutAsync(
                    operation,
                    credentialResult.Value,
                    createAccount: false,
                    operationLabel: "PlayFab Game Center restore login");
                if (!loginResult.IsSuccess)
                {
                    HandleError(loginResult.Error, operation);
                    return;
                }

                ThrowIfOperationInactive(operation);
                _session = loginResult.Value;
                SetLinkedProvider(LinkedProviderGameCenter, operation);
                SetState(AuthState.LoggedInProvider, operation);
                ThrowIfOperationInactive(operation);
                OnLoginSuccess?.Invoke(_session);
                ThrowIfOperationInactive(operation);
                OnUserMessage?.Invoke("Hesabınız başarıyla yüklendi.");
                LogInfo($"[{_currentCorrelationId}] Game Center account loaded: {_session.PlayFabId}");

                CompleteOperation(operation);
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            {
                LogInfo($"Auth operation {operation.Id} ('{operation.Name}') was cancelled.");
            }

            catch (Exception ex)
            {
                LogError($"[{_currentCorrelationId}] Unexpected error in LoadAccountWithGameCenter: {ex}");
                HandleError(AuthError.Generic($"Unexpected error: {ex.Message}"), operation);
            }
#endif
        }

        /// <summary>Link Google account and disable anonymous login</summary>
        public async void LinkGoogleAndDisableAnon()
        {
            if (!TryStartOperation("LinkGoogle", out var operation))
            {
                OnUserMessage?.Invoke("İşlem devam ediyor. Lütfen bekleyin.");
                return;
            }

#if !UNITY_ANDROID || UNITY_EDITOR
            OnUserMessage?.Invoke("Google girişi sadece Android'de desteklenmektedir.");
            CompleteOperation(operation);
            return;
#else
            try
            {
                SetState(AuthState.LinkingProvider, operation);
                ThrowIfOperationInactive(operation);
                _currentCorrelationId = Guid.NewGuid().ToString("N").Substring(0, 8);
                LogInfo($"[{_currentCorrelationId}] Starting Google link");

                // Step 1: Get Google token
                var tokenResult = await GetGoogleServerAuthCodeWithTimeoutAsync(
                    operation,
                    allowInteractive: true,
                    operationLabel: "Google Play Games link auth");
                if (!tokenResult.IsSuccess)
                {
                    HandleError(tokenResult.Error, operation);
                    return;
                }

                // Step 2: Link to PlayFab
                var linkResult = await LinkGoogleWithTimeoutAsync(operation, tokenResult.Value);
                if (!linkResult.IsSuccess)
                {
                    HandleError(linkResult.Error, operation);
                    return;
                }

                SetLinkedProvider(LinkedProviderGoogle, operation);
                LogInfo($"[{_currentCorrelationId}] Google link successful");

                // Step 3: Unlink CustomID
                await UnlinkAnonCustomIdAsync(operation);

                if (_currentState != AuthState.Error)
                {
                    ThrowIfOperationInactive(operation);
                    _session.HasProviderLink = true;
                    SetState(AuthState.LoggedInProvider, operation);
                    ThrowIfOperationInactive(operation);
                    OnUserMessage?.Invoke("Google hesabı başarıyla bağlandı.");
                    LogInfo($"[{_currentCorrelationId}] Google link flow completed");
                }

                CompleteOperation(operation);
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            {
                LogInfo($"Auth operation {operation.Id} ('{operation.Name}') was cancelled.");
            }

            catch (Exception ex)
            {
                LogError($"[{_currentCorrelationId}] Unexpected error in LinkGoogleAndDisableAnon: {ex}");
                HandleError(AuthError.Generic($"Unexpected error: {ex.Message}"), operation);
            }
#endif
        }
        /// <summary>Load existing account via Google (post-reinstall recovery)</summary>
        public async void LoadAccountWithGoogle()
        {
            if (!TryStartOperation("LoadGoogle", out var operation))
            {
                OnUserMessage?.Invoke("İşlem devam ediyor. Lütfen bekleyin.");
                return;
            }

#if !UNITY_ANDROID || UNITY_EDITOR
            OnUserMessage?.Invoke("Google girişi sadece Android'de desteklenmektedir.");
            CompleteOperation(operation);
            return;
#else
            try
            {
                SetState(AuthState.LoggingInAnon, operation); // Reusing state
                ThrowIfOperationInactive(operation);
                _currentCorrelationId = Guid.NewGuid().ToString("N").Substring(0, 8);
                LogInfo($"[{_currentCorrelationId}] Loading account via Google");

                var tokenResult = await GetGoogleServerAuthCodeWithTimeoutAsync(
                    operation,
                    allowInteractive: true,
                    operationLabel: "Google Play Games account restore auth");
                if (!tokenResult.IsSuccess)
                {
                    HandleError(tokenResult.Error, operation);
                    return;
                }

                var loginResult = await LoginWithGoogleWithTimeoutAsync(
                    operation,
                    tokenResult.Value,
                    createAccount: false,
                    operationLabel: "PlayFab Google restore login");
                if (!loginResult.IsSuccess)
                {
                    HandleError(loginResult.Error, operation);
                    return;
                }

                ThrowIfOperationInactive(operation);
                _session = loginResult.Value;
                SetLinkedProvider(LinkedProviderGoogle, operation);
                SetState(AuthState.LoggedInProvider, operation);
                ThrowIfOperationInactive(operation);
                OnLoginSuccess?.Invoke(_session);
                ThrowIfOperationInactive(operation);
                OnUserMessage?.Invoke("Hesabınız başarıyla yüklendi.");
                LogInfo($"[{_currentCorrelationId}] Google account loaded: {_session.PlayFabId}");

                CompleteOperation(operation);
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            {
                LogInfo($"Auth operation {operation.Id} ('{operation.Name}') was cancelled.");
            }

            catch (Exception ex)
            {
                LogError($"[{_currentCorrelationId}] Unexpected error in LoadAccountWithGoogle: {ex}");
                HandleError(AuthError.Generic($"Unexpected error: {ex.Message}"), operation);
            }
#endif
        }
        // === INTERNAL FLOWS ===

        private async Task<bool> TryLoginWithLinkedProviderAsync(AuthOperation operation)
        {
            if (!TryGetLinkedProvider(out var provider))
                return false;

            LogInfo($"[{_currentCorrelationId}] Attempting provider auto login: {provider}");

            if (string.Equals(provider, LinkedProviderGoogle, StringComparison.OrdinalIgnoreCase))
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (_googleProvider == null)
                {
                    LogWarning($"[{_currentCorrelationId}] Google provider not available");
                    return false;
                }

                var tokenResult = await GetGoogleServerAuthCodeWithTimeoutAsync(
                    operation,
                    allowInteractive: false,
                    operationLabel: "Google Play Games linked restore auth");
                if (!tokenResult.IsSuccess)
                {
                    LogWarning($"[{_currentCorrelationId}] Google silent login unavailable: {tokenResult.Error.DebugMessage}");
                    return false;
                }

                var loginResult = await LoginWithGoogleWithTimeoutAsync(
                    operation,
                    tokenResult.Value,
                    createAccount: false,
                    operationLabel: "PlayFab Google linked restore login");
                if (!loginResult.IsSuccess)
                {
                    HandleError(loginResult.Error, operation);
                    return true;
                }

                ThrowIfOperationInactive(operation);
                _session = loginResult.Value;
                SetLinkedProvider(LinkedProviderGoogle, operation);
                SetState(AuthState.LoggedInProvider, operation);
                ThrowIfOperationInactive(operation);
                OnLoginSuccess?.Invoke(_session);
                LogInfo($"[{_currentCorrelationId}] Google auto login successful: {_session.PlayFabId}");
                CompleteOperation(operation);
                return true;
#else
                LogWarning($"[{_currentCorrelationId}] Google auto login skipped - unsupported platform");
                return false;
#endif
            }
            if (string.Equals(provider, LinkedProviderGameCenter, StringComparison.OrdinalIgnoreCase))
            {
#if UNITY_IOS && !UNITY_EDITOR
                if (_gameCenterProvider == null)
                {
                    LogWarning($"[{_currentCorrelationId}] Game Center provider not available");
                    return false;
                }

                var gcResult = await AuthenticateGameCenterWithTimeoutAsync(
                    operation,
                    allowInteractive: false,
                    operationLabel: "Game Center linked restore auth");
                if (!gcResult.IsSuccess)
                {
                    LogWarning($"[{_currentCorrelationId}] Game Center silent login unavailable: {gcResult.Error.DebugMessage}");
                    return false;
                }

                var gcLoginResult = await LoginWithGameCenterWithTimeoutAsync(
                    operation,
                    gcResult.Value,
                    createAccount: false,
                    operationLabel: "PlayFab Game Center linked restore login");
                if (!gcLoginResult.IsSuccess)
                {
                    HandleError(gcLoginResult.Error, operation);
                    return true;
                }

                ThrowIfOperationInactive(operation);
                _session = gcLoginResult.Value;
                SetLinkedProvider(LinkedProviderGameCenter, operation);
                SetState(AuthState.LoggedInProvider, operation);
                ThrowIfOperationInactive(operation);
                OnLoginSuccess?.Invoke(_session);
                LogInfo($"[{_currentCorrelationId}] Game Center auto login successful: {_session.PlayFabId}");
                CompleteOperation(operation);
                return true;
#else
                LogWarning($"[{_currentCorrelationId}] Game Center auto login skipped - unsupported platform");
                return false;
#endif
            }

            LogWarning($"[{_currentCorrelationId}] Unknown linked provider: {provider}");
            return false;
        }

        private async Task LoginAnonWithPersistentCustomIdAsync(AuthOperation operation)
        {
            SetState(AuthState.LoggingInAnon, operation);

            // Get or create persistent key
            var keyResult = await GetPersistentKeyWithTimeoutAsync(operation);
            if (!keyResult.IsSuccess)
            {
                HandleError(keyResult.Error, operation);
                return;
            }

            ThrowIfOperationInactive(operation);
            _persistentCustomId = keyResult.Value;
            LogInfo($"[{_currentCorrelationId}] Persistent key acquired (masked): {MaskKey(_persistentCustomId)}");

            // Login with CustomID
            var loginResult = await LoginWithCustomIdWithTimeoutAsync(operation, _persistentCustomId, createAccount: true);
            if (!loginResult.IsSuccess)
            {
                HandleError(loginResult.Error, operation);
                return;
            }

            ThrowIfOperationInactive(operation);
            _session = loginResult.Value;
            bool isReturningUser = loginResult.Value.HasProviderLink;

            SetState(isReturningUser ? AuthState.LoggedInProvider : AuthState.LoggedInAnon, operation);
            ThrowIfOperationInactive(operation);
            OnLoginSuccess?.Invoke(_session);

            LogInfo($"[{_currentCorrelationId}] Login successful - PlayFabId: {_session.PlayFabId}, HasProvider: {isReturningUser}");
            CompleteOperation(operation);
        }

        private async Task UnlinkAnonCustomIdAsync(AuthOperation operation)
        {
            if (string.IsNullOrEmpty(_persistentCustomId))
            {
                LogWarning($"[{_currentCorrelationId}] UnlinkCustomID skipped - no persistent key");
                return;
            }

            SetState(AuthState.UnlinkingAnon, operation);
            LogInfo($"[{_currentCorrelationId}] Unlinking CustomID");

            var unlinkResult = await UnlinkCustomIdWithTimeoutAsync(operation, _persistentCustomId);

            if (!unlinkResult.IsSuccess)
            {
                // Critical: unlink failed but provider is linked - user should be notified
                LogError($"[{_currentCorrelationId}] UnlinkCustomID failed: {unlinkResult.Error.DebugMessage}");
                HandleError(AuthError.UnlinkFailure(unlinkResult.Error.DebugMessage), operation);
                return;
            }

            LogInfo($"[{_currentCorrelationId}] CustomID unlinked successfully");
        }

        // === STATE & ERROR HANDLING ===

        private void SetState(AuthState newState, AuthOperation operation)
        {
            AuthState oldState;
            Action<AuthState, AuthState> stateChanged;
            string correlationId;

            lock (_stateLock)
            {
                if (!IsOperationCurrentLocked(operation))
                    return;

                if (_currentState == newState)
                    return;

                oldState = _currentState;
                _currentState = newState;
                stateChanged = OnAuthStateChanged;
                correlationId = _currentCorrelationId;
            }

            LogInfo($"[{correlationId}] State: {oldState} → {newState}");
            try
            {
                stateChanged?.Invoke(oldState, newState);
            }
            catch (Exception exception)
            {
                LogError(
                    $"Auth state subscriber threw during {oldState} → {newState}: {exception}");
            }
        }

        private void HandleError(
            AuthError error,
            AuthOperation operation,
            bool cancelOperation = false,
            string cancellationReason = null)
        {
            if (!IsOperationCurrent(operation))
                return;

            try
            {
                SetState(AuthState.Error, operation);
                if (!IsOperationCurrent(operation))
                    return;
                OnUserMessage?.Invoke(error.UserMessage);
                LogError($"[{_currentCorrelationId}] Error: {error.Code} - {error.DebugMessage}");

                if (!error.IsRetryable && IsOperationCurrent(operation))
                    OnFatalError?.Invoke(error);
            }
            finally
            {
                if (cancelOperation)
                {
                    CancelOperation(
                        operation,
                        cancellationReason ?? "Auth operation cancelled after an error.",
                        resetPlatformRequests: true);
                }
                else
                    CompleteOperation(operation);
            }
        }

        private void HandleLinkedProviderRestoreFailure(string provider, AuthOperation operation)
        {
            var providerDisplayName = GetProviderDisplayName(provider);
            HandleError(new AuthError(
                AuthErrorCode.PlatformAuthFailed,
                $"{providerDisplayName} hesabınıza giriş yapılamadı. Lütfen tekrar deneyin.",
                $"Linked provider '{provider}' login failed. Anonymous fallback blocked to avoid fresh-account creation.",
                isRetryable: true), operation);
        }

        private async Task<Result<T>> AwaitResultWithTimeoutAsync<T>(
            AuthOperation operation,
            Task<Result<T>> task,
            float timeoutSeconds,
            AuthError timeoutError,
            string operationLabel)
        {
            ThrowIfOperationInactive(operation);
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            var taskLease = TrackUnderlyingTask(operation, task);
            try
            {
                using var timeoutCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), timeoutCancellation.Token);
                var completedTask = await Task.WhenAny(task, timeoutTask);
                if (completedTask != task)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    LogWarning($"[{_currentCorrelationId}] {operationLabel} timed out after {timeoutSeconds:F1}s");
                    HandleError(
                        timeoutError,
                        operation,
                        cancelOperation: true,
                        cancellationReason:
                            $"{operationLabel} timed out with an unresolved underlying request.");
                    throw new OperationCanceledException(
                        $"{operationLabel} timed out; the auth slot remains quarantined until the request completes.",
                        operation.Token);
                }

                timeoutCancellation.Cancel();
                Result<T> result;
                try
                {
                    result = await task;
                }
                finally
                {
                    taskLease.Release();
                }

                ThrowIfOperationInactive(operation);
                return result;
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogError($"[{_currentCorrelationId}] {operationLabel} threw unexpectedly: {ex}");
                return Result<T>.Failure(AuthError.Generic($"{operationLabel} exception: {ex.Message}"));
            }
        }

        private AuthError CreateTimeoutError(
            AuthErrorCode code,
            string userMessage,
            string operationLabel,
            float timeoutSeconds)
        {
            return new AuthError(
                code,
                userMessage,
                $"{operationLabel} timed out after {timeoutSeconds:F1}s",
                isRetryable: true);
        }

        private Task<Result<string>> GetPersistentKeyWithTimeoutAsync(AuthOperation operation)
        {
            return AwaitResultWithTimeoutAsync(
                operation,
                _secureStorage.GetOrCreatePersistentKeyAsync(),
                SecureStorageTimeoutSeconds,
                CreateTimeoutError(
                    AuthErrorCode.SecureStorageError,
                    "Cihaz anahtarı alınamadı. Lütfen tekrar deneyin.",
                    "Secure storage lookup",
                    SecureStorageTimeoutSeconds),
                "Secure storage lookup");
        }

        private Task<Result<string>> GetGoogleServerAuthCodeWithTimeoutAsync(AuthOperation operation,
            bool allowInteractive,
            string operationLabel)
        {
            var timeoutSeconds = allowInteractive
                ? InteractivePlatformAuthTimeoutSeconds
                : AutomaticPlatformAuthTimeoutSeconds;

            return AwaitResultWithTimeoutAsync(
                operation,
                _googleProvider.GetGoogleServerAuthCodeAsync(allowInteractive),
                timeoutSeconds,
                CreateTimeoutError(
                    AuthErrorCode.PlatformAuthFailed,
                    "Google girişi zaman aşımına uğradı. Lütfen tekrar deneyin.",
                    operationLabel,
                    timeoutSeconds),
                operationLabel);
        }

        private Task<Result<GameCenterCredential>> AuthenticateGameCenterWithTimeoutAsync(AuthOperation operation,
            bool allowInteractive,
            string operationLabel)
        {
            var timeoutSeconds = allowInteractive
                ? InteractivePlatformAuthTimeoutSeconds
                : AutomaticPlatformAuthTimeoutSeconds;

            return AwaitResultWithTimeoutAsync(
                operation,
                _gameCenterProvider.AuthenticateAsync(),
                timeoutSeconds,
                CreateTimeoutError(
                    AuthErrorCode.PlatformAuthFailed,
                    "Game Center girişi zaman aşımına uğradı. Lütfen tekrar deneyin.",
                    operationLabel,
                    timeoutSeconds),
                operationLabel);
        }

        private Task<Result<AuthSessionData>> LoginWithGoogleWithTimeoutAsync(AuthOperation operation,
            string serverAuthCode,
            bool createAccount,
            string operationLabel)
        {
            return AwaitResultWithTimeoutAsync(
                operation,
                _playFabService.LoginWithGoogleAccountAsync(serverAuthCode, createAccount),
                PlayFabRequestTimeoutSeconds,
                CreateTimeoutError(
                    AuthErrorCode.NetworkError,
                    "Sunucu girişi zaman aşımına uğradı. Lütfen tekrar deneyin.",
                    operationLabel,
                    PlayFabRequestTimeoutSeconds),
                operationLabel);
        }

        private Task<Result<AuthSessionData>> LoginWithGameCenterWithTimeoutAsync(AuthOperation operation,
            GameCenterCredential credential,
            bool createAccount,
            string operationLabel)
        {
            return AwaitResultWithTimeoutAsync(
                operation,
                _playFabService.LoginWithGameCenterAsync(credential, createAccount),
                PlayFabRequestTimeoutSeconds,
                CreateTimeoutError(
                    AuthErrorCode.NetworkError,
                    "Sunucu girişi zaman aşımına uğradı. Lütfen tekrar deneyin.",
                    operationLabel,
                    PlayFabRequestTimeoutSeconds),
                operationLabel);
        }

        private Task<Result<AuthSessionData>> LoginWithCustomIdWithTimeoutAsync(AuthOperation operation, string customId, bool createAccount)
        {
            return AwaitResultWithTimeoutAsync(
                operation,
                _playFabService.LoginWithCustomIDAsync(customId, createAccount),
                PlayFabRequestTimeoutSeconds,
                CreateTimeoutError(
                    AuthErrorCode.NetworkError,
                    "Anonim giriş zaman aşımına uğradı. Lütfen tekrar deneyin.",
                    "PlayFab CustomID login",
                    PlayFabRequestTimeoutSeconds),
                "PlayFab CustomID login");
        }

        private Task<Result<bool>> LinkGoogleWithTimeoutAsync(AuthOperation operation, string serverAuthCode)
        {
            return AwaitResultWithTimeoutAsync(
                operation,
                _playFabService.LinkGoogleAccountAsync(serverAuthCode),
                PlayFabRequestTimeoutSeconds,
                CreateTimeoutError(
                    AuthErrorCode.NetworkError,
                    "Google hesabı bağlanırken zaman aşımı oluştu. Lütfen tekrar deneyin.",
                    "PlayFab Google link",
                    PlayFabRequestTimeoutSeconds),
                "PlayFab Google link");
        }

        private Task<Result<bool>> LinkGameCenterWithTimeoutAsync(AuthOperation operation, GameCenterCredential credential)
        {
            return AwaitResultWithTimeoutAsync(
                operation,
                _playFabService.LinkGameCenterAccountAsync(credential),
                PlayFabRequestTimeoutSeconds,
                CreateTimeoutError(
                    AuthErrorCode.NetworkError,
                    "Game Center hesabı bağlanırken zaman aşımı oluştu. Lütfen tekrar deneyin.",
                    "PlayFab Game Center link",
                    PlayFabRequestTimeoutSeconds),
                "PlayFab Game Center link");
        }

        private Task<Result<bool>> UnlinkCustomIdWithTimeoutAsync(AuthOperation operation, string customId)
        {
            return AwaitResultWithTimeoutAsync(
                operation,
                _playFabService.UnlinkCustomIDAsync(customId),
                UnlinkRequestTimeoutSeconds,
                CreateTimeoutError(
                    AuthErrorCode.UnlinkFailed,
                    "Hesap bağlama tamamlanamadı. Lütfen tekrar deneyin.",
                    "PlayFab CustomID unlink",
                    UnlinkRequestTimeoutSeconds),
                "PlayFab CustomID unlink");
        }

        private static string GetProviderDisplayName(string provider)
        {
            if (string.Equals(provider, LinkedProviderGoogle, StringComparison.OrdinalIgnoreCase))
            {
                return "Google Play Games";
            }

            if (string.Equals(provider, LinkedProviderGameCenter, StringComparison.OrdinalIgnoreCase))
            {
                return "Game Center";
            }

            return "Kayıtlı";
        }

        private UnderlyingTaskLease TrackUnderlyingTask(AuthOperation operation, Task task)
        {
            lock (_stateLock)
            {
                if (!IsOperationCurrentLocked(operation))
                    ThrowOperationInactive(operation);

                operation.RegisterUnderlyingTask();
            }

            var lease = new UnderlyingTaskLease(this, operation);
            _ = task.ContinueWith(
                completedTask =>
                {
                    // Observe late faults when the caller already left after timeout/cancellation.
                    _ = completedTask.Exception;
                    lease.Release();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return lease;
        }

        private void ReleaseUnderlyingTask(AuthOperation operation)
        {
            bool shouldDispose = false;
            lock (_stateLock)
            {
                operation.ReleaseUnderlyingTask();
                if (operation.HasPendingUnderlyingTasks
                    || !operation.IsCompletionRequested)
                {
                    return;
                }

                if (ReferenceEquals(_activeOperation, operation))
                    _activeOperation = null;

                shouldDispose = true;
            }

            if (shouldDispose)
                operation.Dispose();
        }

        private bool TryStartOperation(string operationName, out AuthOperation operation)
        {
            if (string.IsNullOrWhiteSpace(operationName))
                throw new ArgumentException("Operation name is required.", nameof(operationName));

            lock (_stateLock)
            {
                var current = _activeOperation;
                if (current != null && current.HasPendingUnderlyingTasks)
                {
                    var pendingElapsed = Time.realtimeSinceStartup - current.StartedAtRealtime;
                    if (!current.IsCancellationRequested
                        && pendingElapsed >= StuckOperationRecoverySeconds)
                    {
                        var timeoutReason =
                            $"Cancelling stale operation '{current.Name}' ({current.Id}) before '{operationName}'. " +
                            "The auth slot remains quarantined until its underlying request completes.";
                        LogWarning(timeoutReason);
                        CancelOperationLocked(current, timeoutReason, resetPlatformRequests: true);
                    }

                    LogWarning(
                        $"Auth operation blocked: {operationName}. " +
                        $"Operation '{current.Name}' ({current.Id}) still has " +
                        $"{current.PendingUnderlyingTaskCount} unresolved underlying request(s).");
                    operation = null;
                    return false;
                }

                if (current != null
                    && (current.IsCancellationRequested || current.IsCompletionRequested))
                {
                    _activeOperation = null;
                    current.Dispose();
                    current = null;
                }

                if (current != null)
                {
                    var elapsed = Time.realtimeSinceStartup - current.StartedAtRealtime;
                    if (elapsed < StuckOperationRecoverySeconds)
                    {
                        LogWarning(
                            $"Concurrent operation blocked: {operationName} " +
                            $"(active: {current.Name}, id: {current.Id}, elapsed: {elapsed:F1}s)");
                        operation = null;
                        return false;
                    }

                    var recoveryReason =
                        $"Starting '{operationName}' after stale operation '{current.Name}' " +
                        $"({current.Id}) exceeded {StuckOperationRecoverySeconds:F1}s.";
                    LogWarning(recoveryReason);
                    CancelOperationLocked(current, recoveryReason, resetPlatformRequests: true);
                }

                if (_activeOperation != null)
                {
                    operation = null;
                    return false;
                }

                operation = new AuthOperation(
                    ++_nextOperationId,
                    operationName,
                    Time.realtimeSinceStartup);
                _activeOperation = operation;
                return true;
            }
        }

        private void CompleteOperation(AuthOperation operation)
        {
            if (operation == null)
                return;

            bool shouldDispose = false;
            lock (_stateLock)
            {
                bool isFirstCompletionRequest = operation.RequestCompletion();
                if (ReferenceEquals(_activeOperation, operation)
                    && !operation.HasPendingUnderlyingTasks)
                {
                    _activeOperation = null;
                    shouldDispose = true;
                }
                else if (ReferenceEquals(_activeOperation, operation)
                         && isFirstCompletionRequest)
                {
                    LogWarning(
                        $"Auth operation '{operation.Name}' ({operation.Id}) completed logically, but " +
                        $"{operation.PendingUnderlyingTaskCount} underlying request(s) are still pending. " +
                        "New auth operations remain blocked until they drain.");
                }
                else if (!operation.HasPendingUnderlyingTasks)
                    shouldDispose = true;
            }

            if (shouldDispose)
                operation.Dispose();
        }

        private bool IsOperationCurrent(AuthOperation operation)
        {
            if (operation == null || operation.IsCancellationRequested)
                return false;

            lock (_stateLock)
            {
                return IsOperationCurrentLocked(operation);
            }
        }

        private bool IsOperationCurrentLocked(AuthOperation operation) =>
            operation != null
            && !operation.IsCancellationRequested
            && !operation.IsCompletionRequested
            && ReferenceEquals(_activeOperation, operation);

        private void ThrowIfOperationInactive(AuthOperation operation)
        {
            if (IsOperationCurrent(operation))
                return;

            ThrowOperationInactive(operation);
        }

        private static void ThrowOperationInactive(AuthOperation operation)
        {
            var token = operation?.Token ?? new CancellationToken(canceled: true);
            throw new OperationCanceledException(
                "The auth operation is no longer active.",
                token);
        }

        private void CancelOperation(
            AuthOperation operation,
            string reason,
            bool resetPlatformRequests)
        {
            lock (_stateLock)
            {
                CancelOperationLocked(operation, reason, resetPlatformRequests);
            }
        }

        private void CancelOperationLocked(
            AuthOperation operation,
            string reason,
            bool resetPlatformRequests)
        {
            if (!ReferenceEquals(_activeOperation, operation))
                return;

            operation.RequestCompletion();
            operation.Cancel();
            if (resetPlatformRequests)
                ResetPlatformAuthRequests(reason);

            if (operation.HasPendingUnderlyingTasks)
            {
                LogWarning(
                    $"Auth operation '{operation.Name}' ({operation.Id}) is quarantined with " +
                    $"{operation.PendingUnderlyingTaskCount} unresolved underlying request(s).");
                return;
            }

            _activeOperation = null;
            operation.Dispose();
        }

        public bool TryRecoverOperationLock(float minimumElapsedSeconds, string reason)
        {
            if (minimumElapsedSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(minimumElapsedSeconds));

            reason = string.IsNullOrWhiteSpace(reason)
                ? "Manual auth operation recovery."
                : reason;

            lock (_stateLock)
            {
                var current = _activeOperation;
                if (current == null)
                    return false;

                if (current.IsCancellationRequested)
                {
                    LogInfo(
                        $"Auth operation '{current.Name}' ({current.Id}) is already cancelled and " +
                        $"waiting for {current.PendingUnderlyingTaskCount} underlying request(s) to drain.");
                    return false;
                }

                var elapsed = Time.realtimeSinceStartup - current.StartedAtRealtime;
                if (elapsed < minimumElapsedSeconds)
                {
                    LogInfo(
                        $"Skip auth operation recovery for '{current.Name}' ({current.Id}) " +
                        $"because elapsed {elapsed:F1}s is below threshold {minimumElapsedSeconds:F1}s. Reason: {reason}");
                    return false;
                }

                LogWarning(
                    $"Recovering auth operation '{current.Name}' ({current.Id}) after {elapsed:F1}s. " +
                    $"Reason: {reason}");
                CancelOperationLocked(current, reason, resetPlatformRequests: true);
                return true;
            }
        }

        private sealed class AuthOperation : IDisposable
        {
            private CancellationTokenSource _cancellation;
            private int _pendingUnderlyingTaskCount;
            private bool _completionRequested;

            public AuthOperation(long id, string name, float startedAtRealtime)
            {
                Id = id;
                Name = name;
                StartedAtRealtime = startedAtRealtime;
                _cancellation = new CancellationTokenSource();
                Token = _cancellation.Token;
            }

            public long Id { get; }
            public string Name { get; }
            public float StartedAtRealtime { get; }
            public CancellationToken Token { get; }
            public bool IsCancellationRequested => Token.IsCancellationRequested;
            public int PendingUnderlyingTaskCount => _pendingUnderlyingTaskCount;
            public bool HasPendingUnderlyingTasks => _pendingUnderlyingTaskCount > 0;
            public bool IsCompletionRequested => _completionRequested;

            public void RegisterUnderlyingTask()
            {
                checked
                {
                    _pendingUnderlyingTaskCount++;
                }
            }

            public void ReleaseUnderlyingTask()
            {
                if (_pendingUnderlyingTaskCount <= 0)
                    throw new InvalidOperationException(
                        "Underlying auth task count is already zero.");

                _pendingUnderlyingTaskCount--;
            }

            public bool RequestCompletion()
            {
                bool wasRequested = _completionRequested;
                _completionRequested = true;
                return !wasRequested;
            }

            public void Cancel()
            {
                var cancellation = Volatile.Read(ref _cancellation);
                if (cancellation == null)
                    return;

                try
                {
                    cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _cancellation, null)?.Dispose();
            }
        }

        private sealed class UnderlyingTaskLease
        {
            private AuthManager _owner;
            private AuthOperation _operation;

            public UnderlyingTaskLease(AuthManager owner, AuthOperation operation)
            {
                _owner = owner;
                _operation = operation;
            }

            public void Release()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                if (owner == null)
                    return;

                var operation = _operation;
                _operation = null;
                owner.ReleaseUnderlyingTask(operation);
            }
        }

        private void ResetPlatformAuthRequests(string reason)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _googleProvider?.ResetInFlightRequest(reason);
#endif

#if UNITY_IOS && !UNITY_EDITOR
            _gameCenterProvider?.ResetInFlightRequest(reason);
#endif
        }

        private bool TryGetLinkedProvider(out string provider)
        {
            provider = PlayerPrefs.GetString(LinkedProviderKey, string.Empty);
            return !string.IsNullOrEmpty(provider);
        }

        private void SetLinkedProvider(string provider, AuthOperation operation)
        {
            ThrowIfOperationInactive(operation);

            if (string.IsNullOrEmpty(provider))
                return;

            PlayerPrefs.SetString(LinkedProviderKey, provider);

#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.Equals(provider, LinkedProviderGoogle, StringComparison.OrdinalIgnoreCase))
            {
                ClearGoogleStartupPromptShown();
            }
#endif

            PlayerPrefs.Save();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void ClearGoogleStartupPromptShown()
        {
            if (!PlayerPrefs.HasKey(GoogleStartupPromptShownKey))
                return;

            PlayerPrefs.DeleteKey(GoogleStartupPromptShownKey);
        }
#endif

        // === LOGGING ===

        private void LogInfo(string message) => Debug.Log($"[AuthManager] {message}");
        private void LogWarning(string message) => Debug.LogWarning($"[AuthManager] {message}");
        private void LogError(string message) => Debug.LogError($"[AuthManager] {message}");

        private string MaskKey(string key) =>
            string.IsNullOrEmpty(key) || key.Length < 8
                ? "***"
                : $"{key.Substring(0, 4)}...{key.Substring(key.Length - 4)}";

        // === PUBLIC ACCESSORS ===

        public AuthState CurrentState => _currentState;
        public AuthSessionData Session => _session;
        public bool IsLoggedIn => _currentState == AuthState.LoggedInAnon ||
                                  _currentState == AuthState.LoggedInProvider;
        public bool IsOperationInProgress
        {
            get
            {
                lock (_stateLock)
                    return _activeOperation != null;
            }
        }
        public bool HasLinkedProviderConfigured => TryGetLinkedProvider(out _);
        public string LinkedProviderName => TryGetLinkedProvider(out var provider) ? provider : string.Empty;
    }
}
