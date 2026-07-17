using UnityEngine;
using UnityEngine.UI;

namespace Serhat.Forge.Auth
{
    public class AuthUI : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private AuthManager _authManager;

        [Header("Link Account Buttons (Anon State)")]
        [SerializeField] private Button _linkGoogleButton;
        [SerializeField] private Button _linkGameCenterButton;

        [Header("Load Account Buttons (Reinstall Recovery)")]
        [SerializeField] private Button _loadAccountGoogleButton;
        [SerializeField] private Button _loadAccountGameCenterButton;

        [Header("Status Display")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _playerIdText;

        [Header("Panels")]
        [SerializeField] private GameObject _anonPanel;
        [SerializeField] private GameObject _providerPanel;
        [SerializeField] private GameObject _loadingPanel;

        private void Start()
        {
            // Try to find AuthManager if not assigned
            if (_authManager == null)
            {
                _authManager = FindAnyObjectByType<AuthManager>();
            }

            if (_authManager == null)
            {
                Debug.LogError("[AuthUI] AuthManager is not assigned and not found in scene!");
                return;
            }

            // Wire up button listeners
            if (_linkGoogleButton != null)
                _linkGoogleButton.onClick.AddListener(() => _authManager.LinkGoogleAndDisableAnon());

            if (_loadAccountGoogleButton != null)
                _loadAccountGoogleButton.onClick.AddListener(() => _authManager.LoadAccountWithGoogle());

            if (_linkGameCenterButton != null)
                _linkGameCenterButton.onClick.AddListener(() => _authManager.LinkGameCenterAndDisableAnon());

            if (_loadAccountGameCenterButton != null)
                _loadAccountGameCenterButton.onClick.AddListener(() => _authManager.LoadAccountWithGameCenter());

            // Subscribe to AuthManager events
            _authManager.OnAuthStateChanged += OnAuthStateChanged;
            _authManager.OnUserMessage += OnUserMessage;
            _authManager.OnLoginSuccess += OnLoginSuccess;
            _authManager.OnFatalError += OnFatalError;

            // Start authentication flow
            _authManager.InitializeAndLogin();
        }

        private void OnDestroy()
        {
            if (_authManager != null)
            {
                _authManager.OnAuthStateChanged -= OnAuthStateChanged;
                _authManager.OnUserMessage -= OnUserMessage;
                _authManager.OnLoginSuccess -= OnLoginSuccess;
                _authManager.OnFatalError -= OnFatalError;
            }
        }

        private void OnAuthStateChanged(AuthState oldState, AuthState newState)
        {
            UpdateStatusText(newState.ToString());

            // Show/hide panels based on state
            bool isAnon = newState == AuthState.LoggedInAnon;
            bool isProvider = newState == AuthState.LoggedInProvider;
            bool isLoading = newState == AuthState.Initializing ||
                           newState == AuthState.LoggingInAnon ||
                           newState == AuthState.LinkingProvider ||
                           newState == AuthState.UnlinkingAnon;

            if (_anonPanel != null)
                _anonPanel.SetActive(isAnon);

            if (_providerPanel != null)
                _providerPanel.SetActive(isProvider);

            if (_loadingPanel != null)
                _loadingPanel.SetActive(isLoading);

            // Platform-specific button visibility
            // Note: Link buttons only shown for anonymous users (not for provider-authenticated users)
#if UNITY_ANDROID
            ShowAndroidButtons(isAnon);
#elif UNITY_IOS
            ShowiOSButtons(isAnon);
#else
            ShowEditorButtons(isAnon);
#endif
        }

        private void ShowAndroidButtons(bool isAnon)
        {
            if (_linkGoogleButton != null)
                _linkGoogleButton.gameObject.SetActive(isAnon);

            if (_loadAccountGoogleButton != null)
                _loadAccountGoogleButton.gameObject.SetActive(isAnon);

            if (_linkGameCenterButton != null)
                _linkGameCenterButton.gameObject.SetActive(false);

            if (_loadAccountGameCenterButton != null)
                _loadAccountGameCenterButton.gameObject.SetActive(false);
        }

        private void ShowiOSButtons(bool isAnon)
        {
            if (_linkGameCenterButton != null)
                _linkGameCenterButton.gameObject.SetActive(isAnon);

            if (_loadAccountGameCenterButton != null)
                _loadAccountGameCenterButton.gameObject.SetActive(isAnon);

            if (_linkGoogleButton != null)
                _linkGoogleButton.gameObject.SetActive(false);

            if (_loadAccountGoogleButton != null)
                _loadAccountGoogleButton.gameObject.SetActive(false);
        }

        private void ShowEditorButtons(bool isAnon)
        {
            // In editor, show all for testing purposes
            if (_linkGoogleButton != null)
                _linkGoogleButton.gameObject.SetActive(isAnon);

            if (_linkGameCenterButton != null)
                _linkGameCenterButton.gameObject.SetActive(isAnon);

            if (_loadAccountGoogleButton != null)
                _loadAccountGoogleButton.gameObject.SetActive(isAnon);

            if (_loadAccountGameCenterButton != null)
                _loadAccountGameCenterButton.gameObject.SetActive(isAnon);
        }

        private void OnUserMessage(string message)
        {
            UpdateStatusText(message);
            Debug.Log($"[AuthUI] User message: {message}");
        }

        private void OnLoginSuccess(AuthSessionData session)
        {
            if (_playerIdText != null)
                _playerIdText.text = $"PlayFab ID: {session.PlayFabId}";

            Debug.Log($"[AuthUI] Logged in: {session.PlayFabId}");
        }

        private void OnFatalError(AuthError error)
        {
            UpdateStatusText($"HATA: {error.UserMessage}");
            Debug.LogError($"[AuthUI] Fatal error: {error.DebugMessage}");
        }

        private void UpdateStatusText(string status)
        {
            if (_statusText != null)
                _statusText.text = status;
        }
    }
}
