using System;
using UnityEngine;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Keeps a page header background extended into the unsafe top area while moving header content below the notch.
    /// Use explicit RectTransform references instead of hierarchy-name lookups.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class PageHeaderSafeAreaController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RectTransform _backgroundRect;
        [SerializeField] private RectTransform[] _contentTargets = Array.Empty<RectTransform>();

        [Header("Layout")]
        [SerializeField] private bool _extendBackgroundIntoUnsafeArea;
        [SerializeField] private float _contentShiftMultiplier = 0.5f;
        [SerializeField] private float _additionalTopPadding;

        private RectTransform _selfRect;
        private RectTransform _rootCanvasRect;
        private Vector2 _originalBackgroundOffsetMin;
        private Vector2 _originalBackgroundOffsetMax;
        private Vector2[] _originalContentPositions = Array.Empty<Vector2>();
        private Rect _lastSafeArea;
        private int _lastScreenWidth = -1;
        private int _lastScreenHeight = -1;
        private float _lastCanvasHeight = -1f;
        private bool _initialized;

        private void Awake()
        {
            InitializeIfNeeded();
        }

        private void OnEnable()
        {
            DynamicCanvasScaler.ScreenConfigurationChanged += HandleScreenConfigurationChanged;
            Refresh(true);
        }

        private void OnDisable()
        {
            DynamicCanvasScaler.ScreenConfigurationChanged -= HandleScreenConfigurationChanged;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            Refresh(false);
        }

        public void Refresh()
        {
            Refresh(true);
        }

        private void HandleScreenConfigurationChanged()
        {
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            if (!InitializeIfNeeded())
            {
                return;
            }

            float canvasHeight = _rootCanvasRect.rect.height;
            int screenHeight = Screen.height;
            if (canvasHeight <= 0f || screenHeight <= 0)
            {
                return;
            }

            Rect safeArea = Screen.safeArea;
            int screenWidth = Screen.width;

            if (!force &&
                safeArea == _lastSafeArea &&
                screenWidth == _lastScreenWidth &&
                screenHeight == _lastScreenHeight &&
                Mathf.Approximately(canvasHeight, _lastCanvasHeight))
            {
                return;
            }

            _lastSafeArea = safeArea;
            _lastScreenWidth = screenWidth;
            _lastScreenHeight = screenHeight;
            _lastCanvasHeight = canvasHeight;

            float topInsetPixels = screenHeight - (safeArea.y + safeArea.height);
            float topInsetCanvas = topInsetPixels / screenHeight * canvasHeight;

            RectTransform backgroundRect = _backgroundRect != null ? _backgroundRect : _selfRect;
            Vector2 adjustedOffsetMin = _originalBackgroundOffsetMin;
            if (_extendBackgroundIntoUnsafeArea)
            {
                adjustedOffsetMin.y -= topInsetCanvas;
            }
            backgroundRect.offsetMin = adjustedOffsetMin;
            backgroundRect.offsetMax = _originalBackgroundOffsetMax;

            float contentOffset = (topInsetCanvas * _contentShiftMultiplier) + _additionalTopPadding;
            for (int i = 0; i < _contentTargets.Length; i++)
            {
                RectTransform target = _contentTargets[i];
                if (target == null)
                {
                    continue;
                }

                Vector2 adjustedPosition = _originalContentPositions[i];
                adjustedPosition.y -= contentOffset;
                target.anchoredPosition = adjustedPosition;
            }
        }

        private bool InitializeIfNeeded()
        {
            if (_selfRect == null)
            {
                _selfRect = GetComponent<RectTransform>();
            }

            if (_backgroundRect == null)
            {
                _backgroundRect = _selfRect;
            }

            Canvas rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
            if (rootCanvas == null)
            {
                return false;
            }

            RectTransform rootCanvasRect = rootCanvas.GetComponent<RectTransform>();
            if (rootCanvasRect == null)
            {
                return false;
            }

            if (_contentTargets == null)
            {
                _contentTargets = Array.Empty<RectTransform>();
            }

            bool contentCountChanged = _originalContentPositions.Length != _contentTargets.Length;
            if (_initialized && _rootCanvasRect == rootCanvasRect && !contentCountChanged)
            {
                return true;
            }

            _rootCanvasRect = rootCanvasRect;

            RectTransform backgroundRect = _backgroundRect != null ? _backgroundRect : _selfRect;
            _originalBackgroundOffsetMin = backgroundRect.offsetMin;
            _originalBackgroundOffsetMax = backgroundRect.offsetMax;

            _originalContentPositions = new Vector2[_contentTargets.Length];
            for (int i = 0; i < _contentTargets.Length; i++)
            {
                _originalContentPositions[i] = _contentTargets[i] != null
                    ? _contentTargets[i].anchoredPosition
                    : Vector2.zero;
            }

            _lastSafeArea = default;
            _lastScreenWidth = -1;
            _lastScreenHeight = -1;
            _lastCanvasHeight = -1f;
            _initialized = true;
            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_contentShiftMultiplier < 0f)
            {
                _contentShiftMultiplier = 0f;
            }

            _initialized = false;
        }
#endif
    }
}
