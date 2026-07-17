#if DOTWEEN
using DG.Tweening;
using UnityEngine;

namespace Serhat.Forge.UI.Navigation
{
    /// <summary>
    /// Animated tooltip used for locked navbar pages.
    /// Opens from source icon center and stays visible until explicitly hidden.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LockedNavbarTooltip : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RectTransform _root;
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("Open")]
        [Range(0f, 1f)]
        [SerializeField] private float _startScaleMultiplier = 0.12f;
        [Min(0.05f)]
        [SerializeField] private float _showDuration = 0.32f;
        [Range(0.6f, 2f)]
        [SerializeField] private float _showBackOvershoot = 1.15f;
        [SerializeField] private Ease _showMoveEase = Ease.OutBack;
        [Min(0f)]
        [SerializeField] private float _fallbackShowYOffset = 10f;

        [Header("Hide")]
        [Range(0f, 1f)]
        [SerializeField] private float _hideScaleMultiplier = 0.82f;
        [Min(0.05f)]
        [SerializeField] private float _hideDuration = 0.18f;
        [SerializeField] private Ease _hideEase = Ease.InBack;

        private Sequence _sequence;
        private Vector3 _visibleScale = Vector3.one;
        private Vector2 _visibleAnchoredPosition;
        private Canvas _parentCanvas;
        private RectTransform _rootParentRect;
        private bool _initialized;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            KillSequence();
        }

        /// <summary>
        /// Plays open animation using fallback origin.
        /// </summary>
        public void Show()
        {
            ShowFrom(null);
        }

        /// <summary>
        /// Plays open animation from source RectTransform center (usually navbar icon).
        /// </summary>
        public void ShowFrom(RectTransform sourceRect)
        {
            EnsureInitialized();
            if (_root == null)
            {
                return;
            }

            KillSequence();

            GameObject rootObject = _root.gameObject;
            if (!rootObject.activeSelf)
            {
                rootObject.SetActive(true);
            }
            _root.SetAsLastSibling();

            Vector2 startPosition = _visibleAnchoredPosition + new Vector2(0f, -_fallbackShowYOffset);
            TryResolveSourceAnchoredPosition(sourceRect, ref startPosition);

            _root.anchoredPosition = startPosition;
            _root.localScale = _visibleScale * _startScaleMultiplier;

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }

            _sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(rootObject, LinkBehaviour.KillOnDestroy);

            if (_canvasGroup != null)
            {
                _sequence.Insert(0f, _canvasGroup.DOFade(1f, _showDuration).SetEase(Ease.OutSine));
            }

            _sequence.Insert(0f, _root.DOScale(_visibleScale, _showDuration).SetEase(_showMoveEase, _showBackOvershoot));
            _sequence.Insert(0f, _root.DOAnchorPos(_visibleAnchoredPosition, _showDuration).SetEase(_showMoveEase));

            _sequence.OnComplete(() =>
            {
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 1f;
                    _canvasGroup.blocksRaycasts = false;
                    _canvasGroup.interactable = false;
                }

                _root.localScale = _visibleScale;
                _root.anchoredPosition = _visibleAnchoredPosition;
                _sequence = null;
            });
        }

        /// <summary>
        /// Hides with animation and deactivates on completion.
        /// </summary>
        public void Hide()
        {
            EnsureInitialized();
            if (_root == null)
            {
                return;
            }

            GameObject rootObject = _root.gameObject;
            if (!rootObject.activeSelf)
            {
                return;
            }

            KillSequence();

            _sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(rootObject, LinkBehaviour.KillOnDestroy);

            if (_canvasGroup != null)
            {
                _sequence.Insert(0f, _canvasGroup.DOFade(0f, _hideDuration).SetEase(Ease.InSine));
            }

            _sequence.Insert(0f, _root.DOScale(_visibleScale * _hideScaleMultiplier, _hideDuration).SetEase(_hideEase));
            _sequence.OnComplete(() =>
            {
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 0f;
                    _canvasGroup.blocksRaycasts = false;
                    _canvasGroup.interactable = false;
                }

                if (rootObject != null)
                {
                    rootObject.SetActive(false);
                }

                _root.localScale = _visibleScale * _hideScaleMultiplier;
                _root.anchoredPosition = _visibleAnchoredPosition;
                _sequence = null;
            });
        }

        /// <summary>
        /// Returns true when given screen point overlaps this tooltip rect.
        /// </summary>
        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            EnsureInitialized();
            if (_root == null || !_root.gameObject.activeInHierarchy)
            {
                return false;
            }

            Camera eventCamera = null;
            if (_parentCanvas != null && _parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                eventCamera = _parentCanvas.worldCamera;
            }

            return RectTransformUtility.RectangleContainsScreenPoint(_root, screenPoint, eventCamera);
        }

        /// <summary>
        /// Hides immediately without animation.
        /// </summary>
        public void HideImmediate()
        {
            EnsureInitialized();
            if (_root == null)
            {
                return;
            }

            KillSequence();

            _root.localScale = _visibleScale * _hideScaleMultiplier;
            _root.anchoredPosition = _visibleAnchoredPosition;

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }

            GameObject rootObject = _root.gameObject;
            if (rootObject.activeSelf)
            {
                rootObject.SetActive(false);
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            if (_root == null)
            {
                _root = transform as RectTransform;
            }

            if (_root == null)
            {
                Debug.LogWarning("[LockedNavbarTooltip] RectTransform is required.", this);
                return;
            }

            if (_canvasGroup == null)
            {
                _canvasGroup = _root.GetComponent<CanvasGroup>();
            }

            if (_parentCanvas == null)
            {
                _parentCanvas = _root.GetComponentInParent<Canvas>();
            }

            _rootParentRect = _root.parent as RectTransform;
            _visibleScale = _root.localScale;
            _visibleAnchoredPosition = _root.anchoredPosition;
            _initialized = true;
        }

        private bool TryResolveSourceAnchoredPosition(RectTransform sourceRect, ref Vector2 resolvedPosition)
        {
            if (sourceRect == null || _rootParentRect == null)
            {
                return false;
            }

            Camera eventCamera = null;
            if (_parentCanvas != null && _parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                eventCamera = _parentCanvas.worldCamera;
            }

            Vector3 worldCenter = sourceRect.TransformPoint(sourceRect.rect.center);
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, worldCenter);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootParentRect, screenPoint, eventCamera, out Vector2 localPoint))
            {
                return false;
            }

            resolvedPosition = localPoint;
            return true;
        }

        private void KillSequence()
        {
            if (_sequence != null && _sequence.IsActive())
            {
                _sequence.Kill();
            }

            _sequence = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _startScaleMultiplier = Mathf.Clamp(_startScaleMultiplier, 0f, 1f);
            _showDuration = Mathf.Max(0.05f, _showDuration);
            _showBackOvershoot = Mathf.Clamp(_showBackOvershoot, 0.6f, 2f);
            _fallbackShowYOffset = Mathf.Max(0f, _fallbackShowYOffset);
            _hideScaleMultiplier = Mathf.Clamp(_hideScaleMultiplier, 0f, 1f);
            _hideDuration = Mathf.Max(0.05f, _hideDuration);
        }
#endif
    }
}
#endif
