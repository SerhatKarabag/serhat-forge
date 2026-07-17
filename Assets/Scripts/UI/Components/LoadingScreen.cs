using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Full-screen loading overlay with animated scrolling pattern and floating logo.
    ///
    /// Lives under a ScreenSpace-Overlay Canvas (UICanvas) which naturally renders
    /// above ScreenSpace-Camera Canvases (GameCanvas). No sorting hacks needed.
    ///
    /// Scene instances can be bound to ILoadingScreen by a scene-level Zenject installer.
    ///
    /// Access via DI: [Inject] private ILoadingScreen _loadingScreen;
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class LoadingScreen : MonoBehaviour, ILoadingScreen
    {
        public event Action Hidden;

        [Header("Pattern Animation")]
        [SerializeField] private RawImage _patternImage;

        [Tooltip("Desired tile size in reference-width pixels. 900 ≈ 1.2 tiles on 1080-wide phone.")]
        [SerializeField] private float _targetTileSize = 900f;

        [Tooltip("UV scroll speed per second. Positive X = right, Positive Y = up.")]
        [SerializeField] private Vector2 _scrollSpeed = new Vector2(0.03f, 0.06f);

        [Header("Logo Animation")]
        [SerializeField] private RectTransform _logo;
        [SerializeField] private float _logoFloatAmount = 30f;
        [SerializeField] private float _logoFloatSpeed = 1.5f;

        [Header("Transition")]
        [Tooltip("Fade-in duration in seconds.")]
        [SerializeField] private float _fadeInDuration = 0.35f;

        [Tooltip("Fade-out duration in seconds.")]
        [SerializeField] private float _fadeOutDuration = 0.25f;

        [Tooltip("Minimum seconds the screen stays visible (prevents flash).")]
        [SerializeField] private float _minimumDisplayTime = 2f;

        // ── Runtime state ───────────────────────────────────────────────
        private CanvasGroup _canvasGroup;
        private Vector2 _uvOffset;
        private Vector3 _logoStartPos;
        private float _showTimestamp;
        private bool _isVisible;
        private bool _initialized;
        private int _hideHoldCount;
        private bool _hideQueued;
        private Action _queuedHideBeforeFadeOut;
        private Action _queuedHideOnComplete;

        // ================================================================
        //  Lifecycle
        // ================================================================

        private void Awake()
        {
            Initialize();
        }

        /// <summary>
        /// One-time setup: cache references and hide via alpha.
        /// Safe to call multiple times — only runs once.
        /// Called from Awake (if active) or from Show() (if started inactive).
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            if (_logo != null)
                _logoStartPos = _logo.anchoredPosition;

            // Start hidden
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            RecalculateTiling();
        }

        private void Update()
        {
            if (!_isVisible) return;

            ScrollPattern();
            FloatLogo();
        }

        // ================================================================
        //  Public API
        // ================================================================

        /// <summary>Fade in the loading screen. Optional callback when fully visible.</summary>
        public void Show(Action onReady = null)
        {
            if (_isVisible) return;

            // Ensure parent is active (e.g. LoadingPanel wrapper)
            if (transform.parent != null && !transform.parent.gameObject.activeSelf)
                transform.parent.gameObject.SetActive(true);

            gameObject.SetActive(true);
            Initialize();

            _isVisible = true;
            _showTimestamp = Time.realtimeSinceStartup;

            RecalculateTiling();

            StopAllCoroutines();

            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;

            StartCoroutine(FadeIn(onReady));
        }

        /// <summary>
        /// Fade out the loading screen. Waits for minimum display time before starting fade.
        /// <paramref name="onBeforeFadeOut"/> fires while the screen is still fully opaque
        /// (alpha = 1), right before the fade-out begins — use it to disable cameras or
        /// switch render targets without a visible flash.
        /// <paramref name="onComplete"/> fires after the fade-out finishes and the screen
        /// is fully transparent.
        /// </summary>
        public void Hide(Action onBeforeFadeOut = null, Action onComplete = null)
        {
            if (!_isVisible) return;
            if (_hideHoldCount > 0)
            {
                _hideQueued = true;
                _queuedHideBeforeFadeOut += onBeforeFadeOut;
                _queuedHideOnComplete += onComplete;
                return;
            }

            StartCoroutine(FadeOut(onBeforeFadeOut, onComplete));
        }

        /// <summary>True while the screen is visible or fading.</summary>
        public bool IsVisible => _isVisible;

        public void AcquireHold()
        {
            _hideHoldCount++;
        }

        public void ReleaseHold()
        {
            if (_hideHoldCount <= 0)
            {
                return;
            }

            _hideHoldCount--;
            if (_hideHoldCount > 0 || !_hideQueued || !_isVisible)
            {
                return;
            }

            var beforeFadeOut = _queuedHideBeforeFadeOut;
            var onComplete = _queuedHideOnComplete;
            _hideQueued = false;
            _queuedHideBeforeFadeOut = null;
            _queuedHideOnComplete = null;
            StartCoroutine(FadeOut(beforeFadeOut, onComplete));
        }

        // ================================================================
        //  Fade coroutines
        // ================================================================

        private IEnumerator FadeIn(Action onReady)
        {
            float t = 0f;
            while (t < _fadeInDuration)
            {
                t += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Clamp01(t / _fadeInDuration);
                yield return null;
            }
            _canvasGroup.alpha = 1f;
            onReady?.Invoke();
        }

        private IEnumerator FadeOut(Action onBeforeFadeOut, Action onComplete)
        {
            // Wait minimum display time
            float elapsed = Time.realtimeSinceStartup - _showTimestamp;
            float delay = Mathf.Max(0f, _minimumDisplayTime - elapsed);
            if (delay > 0f)
            {
                float waited = 0f;
                while (waited < delay)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            // Fire callback while still fully opaque
            onBeforeFadeOut?.Invoke();

            // Fade out
            float t = 0f;
            while (t < _fadeOutDuration)
            {
                t += Time.unscaledDeltaTime;
                _canvasGroup.alpha = 1f - Mathf.Clamp01(t / _fadeOutDuration);
                yield return null;
            }
            _canvasGroup.alpha = 0f;

            _isVisible = false;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
            Hidden?.Invoke();
            onComplete?.Invoke();
        }

        // ================================================================
        //  Internal animation
        // ================================================================

        private void RecalculateTiling()
        {
            if (_patternImage == null || _patternImage.texture == null) return;
            if (_targetTileSize <= 0f) return;

            float screenW = Screen.width;
            float screenH = Screen.height;

            float tilesX = screenW / _targetTileSize;
            float tilesY = tilesX * (screenH / screenW);

            var rect = _patternImage.uvRect;
            rect.width = tilesX;
            rect.height = tilesY;
            _patternImage.uvRect = rect;
        }

        private void ScrollPattern()
        {
            if (_patternImage == null) return;

            _uvOffset += _scrollSpeed * Time.deltaTime;
            _uvOffset.x %= 1f;
            _uvOffset.y %= 1f;

            var rect = _patternImage.uvRect;
            rect.x = _uvOffset.x;
            rect.y = _uvOffset.y;
            _patternImage.uvRect = rect;
        }

        private void FloatLogo()
        {
            if (_logo == null) return;

            var offset = Mathf.Sin(Time.time * _logoFloatSpeed) * _logoFloatAmount;
            _logo.anchoredPosition = _logoStartPos + new Vector3(0f, offset, 0f);
        }
    }
}
