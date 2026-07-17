using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Zenject;

namespace Serhat.Forge.Ads
{
    /// <summary>
    /// Rewarded ad button with injected provider and explicit registration lifecycle.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class AdButton : MonoBehaviour, IAdButton
    {
        [Header("Loading UI")]
        [SerializeField] private GameObject loadingOverlay;

        [Header("Disabled UI")]
        [SerializeField, Range(0.05f, 1f)] private float disabledAlpha = 0.45f;

        [Header("Reward")]
        public UnityEvent OnRewardGranted;

        private IAdService _adService;
        private Button _button;
        private CanvasGroup _canvasGroup;
        private bool _isLoading;
        private bool _isAvailable = true;
        private bool _isRegistered;

        public GameObject GameObject => gameObject;

        [Inject]
        private void Construct(IAdService adService)
        {
            _adService = adService;
            RegisterIfNeeded();
        }

        private void Awake()
        {
            _button = GetComponent<Button>();
            _canvasGroup = GetOrAddCanvasGroup();
        }

        private void OnEnable()
        {
            RegisterIfNeeded();
            UpdateVisualState();
        }

        private void OnDisable()
        {
            UnregisterIfNeeded();
        }

        public void SetAvailable(bool isAvailable)
        {
            _isAvailable = isAvailable;
            UpdateVisualState();
        }

        public void SetLoading(bool isLoading)
        {
            _isLoading = isLoading;
            UpdateVisualState();
        }

        /// <summary>Wire to Button.onClick in the Inspector.</summary>
        public void OnClickAdButton()
        {
            if (_isLoading || _adService == null)
            {
                return;
            }

            _adService.ShowRewarded(() => OnRewardGranted?.Invoke());
        }

        private void RegisterIfNeeded()
        {
            if (_isRegistered || !isActiveAndEnabled || _adService == null)
            {
                return;
            }

            _adService.RegisterAdButton(this);
            _isRegistered = true;
        }

        private void UnregisterIfNeeded()
        {
            if (!_isRegistered || _adService == null)
            {
                return;
            }

            _adService.UnregisterAdButton(this);
            _isRegistered = false;
        }

        private void UpdateVisualState()
        {
            if (loadingOverlay != null)
            {
                loadingOverlay.SetActive(_isAvailable && _isLoading);
            }

            if (_button != null)
            {
                _button.interactable = _isAvailable && !_isLoading;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = _isAvailable ? 1f : disabledAlpha;
                _canvasGroup.interactable = _isAvailable && !_isLoading;
                _canvasGroup.blocksRaycasts = _isAvailable && !_isLoading;
            }
        }

        private CanvasGroup GetOrAddCanvasGroup()
        {
            if (!TryGetComponent(out CanvasGroup canvasGroup))
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            return canvasGroup;
        }
    }
}