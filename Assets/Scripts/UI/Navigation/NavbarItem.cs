#if DOTWEEN
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Serhat.Forge.UI.Navigation
{
    /// <summary>
    /// Individual navbar button with smooth visual transitions.
    /// Handles scaling, color, and optional sprite swapping for active/inactive states.
    /// Interaction blocking is handled by PageNavbar's CanvasGroup.
    /// </summary>
    public class NavbarItem : MonoBehaviour, IPointerClickHandler
    {
        #region Inspector Fields

        [Header("Visual Elements")]
        [SerializeField] private RectTransform _iconTransform;
        [SerializeField] private Image _iconImage;
        [SerializeField] private Image _backgroundImage;

        [Header("Active State")]
        [SerializeField] private float _activeScale = 1.2f;
        [SerializeField] private float _activeYOffset = 20f;
        [SerializeField] private Color _activeIconColor = Color.white;
        [SerializeField] private Color _activeBackgroundColor = new Color(1f, 1f, 1f, 0.2f);
        [SerializeField] private Sprite _activeSprite;

        [Header("Inactive State")]
        [SerializeField] private float _inactiveScale = 1.0f;
        [SerializeField] private float _inactiveYOffset = 0f;
        [SerializeField] private Color _inactiveIconColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color _inactiveBackgroundColor = new Color(1f, 1f, 1f, 0f);
        [SerializeField] private Sprite _inactiveSprite;

        [Header("Page Name Visual (Optional)")]
        [Tooltip("Image displayed under icon when this page is active.")]
        [SerializeField] private Image _pageNameImage;
        [Tooltip("Optional explicit transform for page name image. If empty, pageNameImage RectTransform is used.")]
        [SerializeField] private RectTransform _pageNameTransform;
        [SerializeField, Range(0f, 1f)] private float _inactivePageNameAlpha = 0f;
        [SerializeField, Range(0f, 1f)] private float _activePageNameAlpha = 1f;
        [SerializeField] private float _inactivePageNameScale = 0.92f;
        [SerializeField] private float _activePageNameScale = 1f;
        [SerializeField] private float _inactivePageNameYOffset = -8f;
        [SerializeField] private float _activePageNameYOffset = 0f;

        #endregion

        #region Events

        /// <summary>
        /// Fired when this navbar item is tapped.
        /// Parameter: this NavbarItem instance
        /// </summary>
        public event Action<NavbarItem> OnItemClicked;

        #endregion

        #region Private Fields

        private int _pageIndex;
        private Vector3 _scaleVector = Vector3.one;
        private Vector2 _initialAnchoredPosition;
        private Vector2 _pageNameInitialAnchoredPosition;

        #endregion

        #region Properties

        public int PageIndex => _pageIndex;
        public RectTransform IconTransform => _iconTransform != null ? _iconTransform : transform as RectTransform;

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize this navbar item with its page index.
        /// </summary>
        public void Initialize(int pageIndex)
        {
            _pageIndex = pageIndex;

            if (_iconTransform != null)
            {
                _initialAnchoredPosition = _iconTransform.anchoredPosition;
            }

            if (_pageNameImage != null && _pageNameTransform == null)
            {
                _pageNameTransform = _pageNameImage.rectTransform;
            }

            if (_pageNameTransform != null)
            {
                _pageNameInitialAnchoredPosition = _pageNameTransform.anchoredPosition;
            }
        }

        #endregion

        #region Click Handler

        public void OnPointerClick(PointerEventData eventData)
        {
            // CanvasGroup on parent handles blocking during drag/snap
            OnItemClicked?.Invoke(this);
        }

        #endregion

        #region Visual State

        /// <summary>
        /// Set the visual state instantly (no transition).
        /// </summary>
        /// <param name="active">True for active/selected state, false for inactive</param>
        public void SetActiveState(bool active)
        {
            SetVisualProgress(active ? 1f : 0f);
        }

        /// <summary>
        /// Set visual state with a progress value.
        /// 0 = fully inactive, 1 = fully active.
        /// Used for smooth transitions during page swipe.
        /// </summary>
        /// <param name="progress">0-1 progress toward active state</param>
        public void SetVisualProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);

            // Scale and Y offset interpolation
            if (_iconTransform != null)
            {
                float scale = Mathf.Lerp(_inactiveScale, _activeScale, progress);
                _scaleVector.x = scale;
                _scaleVector.y = scale;
                _scaleVector.z = 1f;
                _iconTransform.localScale = _scaleVector;

                float yOffset = Mathf.Lerp(_inactiveYOffset, _activeYOffset, progress);
                _iconTransform.anchoredPosition = new Vector2(_initialAnchoredPosition.x, _initialAnchoredPosition.y + yOffset);
            }

            // Icon color interpolation
            if (_iconImage != null)
            {
                _iconImage.color = Color.Lerp(_inactiveIconColor, _activeIconColor, progress);

                // Sprite swap at midpoint (if sprites are defined)
                if (_activeSprite != null && _inactiveSprite != null)
                {
                    _iconImage.sprite = progress > 0.5f ? _activeSprite : _inactiveSprite;
                }
            }

            // Background color interpolation
            if (_backgroundImage != null)
            {
                _backgroundImage.color = Color.Lerp(_inactiveBackgroundColor, _activeBackgroundColor, progress);
            }

            ApplyPageNameVisual(progress);
        }

        private void ApplyPageNameVisual(float progress)
        {
            if (_pageNameImage == null && _pageNameTransform == null)
            {
                return;
            }

            if (_pageNameImage != null && _pageNameTransform == null)
            {
                _pageNameTransform = _pageNameImage.rectTransform;
                _pageNameInitialAnchoredPosition = _pageNameTransform.anchoredPosition;
            }

            if (_pageNameTransform != null)
            {
                float pageNameScale = Mathf.Lerp(_inactivePageNameScale, _activePageNameScale, progress);
                _scaleVector.x = pageNameScale;
                _scaleVector.y = pageNameScale;
                _scaleVector.z = 1f;
                _pageNameTransform.localScale = _scaleVector;

                float yOffset = Mathf.Lerp(_inactivePageNameYOffset, _activePageNameYOffset, progress);
                _pageNameTransform.anchoredPosition =
                    new Vector2(_pageNameInitialAnchoredPosition.x, _pageNameInitialAnchoredPosition.y + yOffset);
            }

            if (_pageNameImage != null)
            {
                Color pageNameColor = _pageNameImage.color;
                pageNameColor.a = Mathf.Lerp(_inactivePageNameAlpha, _activePageNameAlpha, progress);
                _pageNameImage.color = pageNameColor;
            }
        }

        #endregion

        #region Editor Helpers

#if UNITY_EDITOR
        private void OnValidate()
        {
            _activeScale = Mathf.Max(0.1f, _activeScale);
            _inactiveScale = Mathf.Max(0.1f, _inactiveScale);
            _activePageNameScale = Mathf.Max(0.1f, _activePageNameScale);
            _inactivePageNameScale = Mathf.Max(0.1f, _inactivePageNameScale);
            _activePageNameAlpha = Mathf.Clamp01(_activePageNameAlpha);
            _inactivePageNameAlpha = Mathf.Clamp01(_inactivePageNameAlpha);

            if (_pageNameImage != null && _pageNameTransform == null)
            {
                _pageNameTransform = _pageNameImage.rectTransform;
            }
        }

        /// <summary>
        /// Editor helper to quickly preview active state.
        /// </summary>
        [ContextMenu("Preview Active State")]
        private void PreviewActive()
        {
            SetActiveState(true);
        }

        /// <summary>
        /// Editor helper to quickly preview inactive state.
        /// </summary>
        [ContextMenu("Preview Inactive State")]
        private void PreviewInactive()
        {
            SetActiveState(false);
        }
#endif

        #endregion
    }
}
#endif
