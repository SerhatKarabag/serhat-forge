#if DOTWEEN
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;
using Serhat.Forge.Core;

namespace Serhat.Forge.UI.Navigation
{
    /// <summary>
    /// Handles horizontal page swiping with snap-to-page behavior.
    /// Supports N pages, smooth 1:1 drag tracking, and velocity-based snap decisions.
    /// Hard-clamped at edges (no overscroll/elastic stretch).
    /// Uses SinglePointerGuard to enforce single-pointer behavior.
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public class SwipePageController : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        #region Inspector Fields

        [Header("Page Configuration")]
        [SerializeField] private int _pageCount = 5;

        [Header("Start Page")]
        [Tooltip("If true, starts on the middle page. If false, uses Start Page Index.")]
        [SerializeField] private bool _startAtMiddlePage = true;

        [Tooltip("For even page counts: if true, use left-middle (n/2-1), if false, use right-middle (n/2).")]
        [SerializeField] private bool _preferLeftMiddle = true;

        [Tooltip("Manual start page index (used when Start At Middle Page is false).")]
        [SerializeField] private int _startPageIndex = 0;

        [Header("Allowed Page Range")]
        [Tooltip("If enabled, navigation is restricted to the configured page range.")]
        [SerializeField] private bool _useAllowedPageRange;

        [Tooltip("Minimum allowed page index when range lock is enabled.")]
        [SerializeField] private int _minAllowedPageIndex;

        [Tooltip("Maximum allowed page index when range lock is enabled. -1 means last page.")]
        [SerializeField] private int _maxAllowedPageIndex = -1;

        [Header("Snap Thresholds")]
        [Tooltip("Percentage of viewport width required to trigger page change (0-1)")]
        [Range(0.1f, 0.5f)]
        [SerializeField] private float _distanceThreshold = 0.25f;

        [Tooltip("Minimum swipe velocity (pixels/second) to trigger page change")]
        [SerializeField] private float _velocityThreshold = 500f;

        [Header("Animation")]
        [SerializeField] private float _snapDuration = 0.35f;
        [SerializeField] private Ease _snapEase = Ease.OutCubic;

        [Header("References")]
        [SerializeField] private RectTransform _content;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private PageContentLayout _pageContentLayout;

        #endregion

        #region Events

        /// <summary>
        /// Fired continuously during drag/animation with transition progress.
        /// Parameters: fromPageIndex, toPageIndex, progress (0-1)
        /// </summary>
        public event Action<int, int, float> OnPageTransitionProgress;

        /// <summary>
        /// Fired when page transition completes (snap finished).
        /// Parameter: newPageIndex
        /// </summary>
        public event Action<int> OnPageChanged;

        /// <summary>
        /// Fired when drag or snap animation starts. Use to block other UI.
        /// </summary>
        public event Action OnBusyStateEnter;

        /// <summary>
        /// Fired when drag and snap animation both complete. Use to unblock other UI.
        /// </summary>
        public event Action OnBusyStateExit;

        #endregion

        #region Private Fields

        private ScrollRect _scrollRect;
        private Tweener _snapTween;

        private int _currentPageIndex;
        private int _targetPageIndex;

        private bool _isDragging;
        private float _dragStartPositionX;
        private float _dragStartTime;
        private float _viewportWidth;
        private float _viewportHeight;
        private float _contentTotalWidth;

        // Edge clamp bounds (content X position)
        private float _minContentX; // Rightmost page position (most negative)
        private float _maxContentX; // Leftmost page position (0 for page 0)

        // Cached for zero-alloc updates
        private Vector2 _tempAnchoredPos;

        // Pointer tracking for single-pointer enforcement
        private int _activePointerId = -1;

        // Whether initial page positioning has been done with valid viewport
        private bool _initialized;

        #endregion

        #region Properties

        public int CurrentPageIndex => _currentPageIndex;
        public int PageCount => _pageCount;
        public int MinAllowedPageIndex => GetEffectiveMinAllowedPageIndex();
        public int MaxAllowedPageIndex => GetEffectiveMaxAllowedPageIndex();
        public bool IsAllowedPageRangeEnabled => _useAllowedPageRange;
        public bool IsDragging => _isDragging;
        public bool IsAnimating => _snapTween != null && _snapTween.IsActive() && _snapTween.IsPlaying();

        /// <summary>
        /// Returns true if currently dragging OR animating (snap tween running).
        /// </summary>
        public bool IsBusy => _isDragging || IsAnimating;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _scrollRect = GetComponent<ScrollRect>();

            // Validate references
            if (_content == null)
                _content = _scrollRect.content;
            if (_viewport == null)
                _viewport = _scrollRect.viewport;

            // Configure ScrollRect for our paging needs
            ConfigureScrollRect();
        }

        private void Start()
        {
            // Initial positioning is deferred to LateUpdate via DetectViewportChange.
            // This ensures DynamicCanvasScaler and all layout systems have finished
            // adjusting before we read viewport dimensions and position content.
            // We cache the desired start page so DetectViewportChange can use it.
            NormalizeAllowedPageRange();
            _currentPageIndex = CalculateStartPageIndex();
            _targetPageIndex = _currentPageIndex;
        }

        private void Update()
        {
            // Continuously broadcast progress during drag or animation
            if (_isDragging || IsAnimating)
            {
                BroadcastTransitionProgress();
            }
        }

        private void LateUpdate()
        {
            // Detect viewport size changes caused by DynamicCanvasScaler,
            // orientation changes, or any layout rebuild. Re-cache dimensions
            // and re-align content to the current page immediately.
            DetectViewportChange();

            // Always clamp content position to prevent overscroll.
            ClampContentPosition();
        }

        private void OnDestroy()
        {
            KillSnapTween();
        }

        #endregion

        #region Initialization

        private void ConfigureScrollRect()
        {
            _scrollRect.horizontal = true;
            _scrollRect.vertical = false;
            _scrollRect.inertia = false; // We handle momentum ourselves
            _scrollRect.movementType = ScrollRect.MovementType.Unrestricted; // We clamp manually in LateUpdate
            _scrollRect.scrollSensitivity = 1f;
        }

        private void CacheViewportDimensions()
        {
            _viewportWidth = _viewport.rect.width;
            _viewportHeight = _viewport.rect.height;
            _contentTotalWidth = _viewportWidth * _pageCount;

            // Set content size to fit all pages
            _content.sizeDelta = new Vector2(_contentTotalWidth, _content.sizeDelta.y);

            // Calculate edge bounds
            int minAllowedPage = GetEffectiveMinAllowedPageIndex();
            int maxAllowedPage = GetEffectiveMaxAllowedPageIndex();

            _maxContentX = GetPagePositionX(minAllowedPage);
            _minContentX = GetPagePositionX(maxAllowedPage);
        }

        private int CalculateStartPageIndex()
        {
            if (!_startAtMiddlePage)
            {
                return ClampToAllowedPageRange(Mathf.Clamp(_startPageIndex, 0, _pageCount - 1));
            }

            // Calculate middle page
            if (_pageCount <= 1)
                return ClampToAllowedPageRange(0);

            // Odd page count: exact middle
            // Even page count: left-middle or right-middle based on preference
            if (_pageCount % 2 == 1)
            {
                // Odd: 5 pages -> index 2
                return ClampToAllowedPageRange(_pageCount / 2);
            }
            else
            {
                // Even: 4 pages -> index 1 (left-middle) or index 2 (right-middle)
                if (_preferLeftMiddle)
                    return ClampToAllowedPageRange((_pageCount / 2) - 1);
                else
                    return ClampToAllowedPageRange(_pageCount / 2);
            }
        }

        private int GetEffectiveMinAllowedPageIndex()
        {
            if (!_useAllowedPageRange)
            {
                return 0;
            }

            return Mathf.Clamp(_minAllowedPageIndex, 0, _pageCount - 1);
        }

        private int GetEffectiveMaxAllowedPageIndex()
        {
            if (!_useAllowedPageRange)
            {
                return _pageCount - 1;
            }

            int maxPage = _maxAllowedPageIndex < 0 ? _pageCount - 1 : _maxAllowedPageIndex;
            maxPage = Mathf.Clamp(maxPage, 0, _pageCount - 1);

            int minPage = GetEffectiveMinAllowedPageIndex();
            if (maxPage < minPage)
            {
                maxPage = minPage;
            }

            return maxPage;
        }

        private int ClampToAllowedPageRange(int pageIndex)
        {
            return Mathf.Clamp(pageIndex, GetEffectiveMinAllowedPageIndex(), GetEffectiveMaxAllowedPageIndex());
        }

        private void NormalizeAllowedPageRange()
        {
            _minAllowedPageIndex = Mathf.Max(0, _minAllowedPageIndex);
            if (_maxAllowedPageIndex < -1)
            {
                _maxAllowedPageIndex = -1;
            }

            if (!_useAllowedPageRange)
            {
                return;
            }

            _minAllowedPageIndex = Mathf.Clamp(_minAllowedPageIndex, 0, Mathf.Max(0, _pageCount - 1));

            if (_maxAllowedPageIndex >= 0)
            {
                _maxAllowedPageIndex = Mathf.Clamp(_maxAllowedPageIndex, 0, Mathf.Max(0, _pageCount - 1));
                if (_maxAllowedPageIndex < _minAllowedPageIndex)
                {
                    _maxAllowedPageIndex = _minAllowedPageIndex;
                }
            }
        }

        #endregion

        #region Viewport Change Detection

        private void DetectViewportChange()
        {
            // Don't re-align while user is actively dragging or animation is running
            if (_isDragging || IsAnimating) return;

            float currentWidth = _viewport.rect.width;
            float currentHeight = _viewport.rect.height;

            // Skip if viewport hasn't been laid out yet
            if (currentWidth < 1f || currentHeight < 1f) return;

            bool widthChanged = !Mathf.Approximately(currentWidth, _viewportWidth);
            bool heightChanged = !Mathf.Approximately(currentHeight, _viewportHeight);

            // First valid frame: initialize dimensions and snap to start page
            if (!_initialized)
            {
                _initialized = true;
                RelayoutAndAlign();
                return;
            }

            // Subsequent frames: re-align if viewport size changed
            // (DynamicCanvasScaler, orientation change, etc.)
            if (widthChanged || heightChanged)
            {
                RelayoutAndAlign();
            }
        }

        /// <summary>
        /// Re-layout child pages, re-cache viewport dimensions, and re-align
        /// content to the current page. Call whenever viewport size changes.
        /// </summary>
        private void RelayoutAndAlign()
        {
            // 1. Re-layout child pages to match new viewport size
            if (_pageContentLayout != null)
            {
                _pageContentLayout.LayoutPages();
            }

            // 2. Re-cache viewport dimensions and edge bounds
            CacheViewportDimensions();

            // 3. Re-align content to current page
            SetPageImmediate(_currentPageIndex);
        }

        #endregion

        #region Edge Clamping

        private void ClampContentPosition()
        {
            // Don't clamp before initialization — bounds are not yet valid
            if (!_initialized) return;

            float currentX = _content.anchoredPosition.x;
            float clampedX = Mathf.Clamp(currentX, _minContentX, _maxContentX);

            if (!Mathf.Approximately(currentX, clampedX))
            {
                _tempAnchoredPos = _content.anchoredPosition;
                _tempAnchoredPos.x = clampedX;
                _content.anchoredPosition = _tempAnchoredPos;
            }
        }

        #endregion

        #region Drag Handlers

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Single-pointer enforcement: block if another pointer is active
            if (!SinglePointerGuard.TryClaimPointer(eventData.pointerId))
                return;

            // Ignore non-horizontal drags
            if (Mathf.Abs(eventData.delta.x) < Mathf.Abs(eventData.delta.y))
            {
                SinglePointerGuard.ReleasePointer(eventData.pointerId);
                return;
            }

            // Kill any running animation immediately - user takes over (snappy)
            KillSnapTween();

            _activePointerId = eventData.pointerId;
            _isDragging = true;
            _dragStartPositionX = _content.anchoredPosition.x;
            _dragStartTime = Time.unscaledTime;

            // Notify listeners that we're busy (block navbar, etc.)
            OnBusyStateEnter?.Invoke();
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Ignore events from non-active pointers
            if (eventData.pointerId != _activePointerId)
                return;

            // ScrollRect handles the 1:1 movement automatically
            // Clamping is done in LateUpdate to catch all movement
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            // If drag was rejected (e.g. started vertical) but ScrollRect
            // still moved content, snap to nearest page once on finger lift.
            if (!_isDragging)
            {
                float currentX = _content.anchoredPosition.x;
                float expectedX = GetPagePositionX(_currentPageIndex);
                if (Mathf.Abs(currentX - expectedX) > 2f)
                {
                    SnapToPage(GetNearestPageIndex());
                }
                return;
            }

            // Ignore events from non-active pointers
            if (eventData.pointerId != _activePointerId)
                return;

            _isDragging = false;
            _activePointerId = -1;
            SinglePointerGuard.ReleasePointer(eventData.pointerId);

            // Final clamp before calculating velocity
            ClampContentPosition();

            float dragDelta = _content.anchoredPosition.x - _dragStartPositionX;
            float dragTime = Time.unscaledTime - _dragStartTime;
            float velocity = dragTime > 0.001f ? dragDelta / dragTime : 0f;

            // Determine target page based on drag distance and velocity
            int targetPage = DetermineTargetPage(dragDelta, velocity);

            // Animate to target
            SnapToPage(targetPage);
        }

        #endregion

        #region Page Navigation Logic

        private int DetermineTargetPage(float dragDelta, float velocity)
        {
            float dragPercent = Mathf.Abs(dragDelta) / _viewportWidth;
            bool draggedRight = dragDelta > 0; // Content moved right = user swiped right = go to previous page
            bool swipedFast = Mathf.Abs(velocity) > _velocityThreshold;
            bool draggedFar = dragPercent > _distanceThreshold;

            int newTarget = _currentPageIndex;

            if (draggedFar || swipedFast)
            {
                // Drag right (content.x increases) = go to previous page (lower index)
                // Drag left (content.x decreases) = go to next page (higher index)
                if (draggedRight || (swipedFast && velocity > 0))
                {
                    newTarget = _currentPageIndex - 1;
                }
                else
                {
                    newTarget = _currentPageIndex + 1;
                }
            }
            else
            {
                // Snap to nearest page based on current position
                newTarget = GetNearestPageIndex();
            }

            // Clamp to valid range
            return ClampToAllowedPageRange(newTarget);
        }

        private int GetNearestPageIndex()
        {
            float currentX = _content.anchoredPosition.x;
            float normalizedPos = -currentX / _viewportWidth;
            return ClampToAllowedPageRange(Mathf.RoundToInt(normalizedPos));
        }

        private float GetPagePositionX(int pageIndex)
        {
            // Page 0 = x:0, Page 1 = x:-viewportWidth, etc.
            return -pageIndex * _viewportWidth;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Navigate to a specific page with animation.
        /// Safe to call during drag or animation - will interrupt current action immediately.
        /// </summary>
        public void GoToPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _pageCount)
                return;
            if (!IsPageIndexAllowed(pageIndex))
                return;

            // Stop any current interaction immediately
            _isDragging = false;
            KillSnapTween();

            SnapToPage(pageIndex);
        }

        /// <summary>
        /// Instantly jump to a page without animation.
        /// </summary>
        public void SetPageImmediate(int pageIndex)
        {
            pageIndex = ClampToAllowedPageRange(Mathf.Clamp(pageIndex, 0, _pageCount - 1));

            KillSnapTween();
            _isDragging = false;

            _currentPageIndex = pageIndex;
            _targetPageIndex = pageIndex;

            _tempAnchoredPos = _content.anchoredPosition;
            _tempAnchoredPos.x = GetPagePositionX(pageIndex);
            _content.anchoredPosition = _tempAnchoredPos;

            // Broadcast final state
            OnPageTransitionProgress?.Invoke(pageIndex, pageIndex, 1f);
            OnPageChanged?.Invoke(pageIndex);
        }

        /// <summary>
        /// Update page count at runtime. Resets to calculated start page.
        /// </summary>
        public void SetPageCount(int count)
        {
            _pageCount = Mathf.Max(1, count);
            NormalizeAllowedPageRange();
            CacheViewportDimensions();
            int startPage = CalculateStartPageIndex();
            SetPageImmediate(startPage);
        }

        /// <summary>
        /// True if the given page index is within the active allowed range.
        /// </summary>
        public bool IsPageIndexAllowed(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _pageCount)
            {
                return false;
            }

            return pageIndex >= GetEffectiveMinAllowedPageIndex() &&
                   pageIndex <= GetEffectiveMaxAllowedPageIndex();
        }

        /// <summary>
        /// Restrict navigation to [minPageIndex, maxPageIndex] (inclusive).
        /// </summary>
        public void SetAllowedPageRange(int minPageIndex, int maxPageIndex)
        {
            _useAllowedPageRange = true;
            _minAllowedPageIndex = minPageIndex;
            _maxAllowedPageIndex = maxPageIndex;
            NormalizeAllowedPageRange();

            int clampedCurrentPage = ClampToAllowedPageRange(_currentPageIndex);
            _currentPageIndex = clampedCurrentPage;
            _targetPageIndex = clampedCurrentPage;

            if (_initialized)
            {
                CacheViewportDimensions();
                SetPageImmediate(clampedCurrentPage);
            }
        }

        /// <summary>
        /// Remove range restriction and allow full [0..pageCount-1] navigation.
        /// </summary>
        public void ClearAllowedPageRange()
        {
            _useAllowedPageRange = false;
            _minAllowedPageIndex = 0;
            _maxAllowedPageIndex = -1;

            if (_initialized)
            {
                CacheViewportDimensions();
                SetPageImmediate(Mathf.Clamp(_currentPageIndex, 0, _pageCount - 1));
            }
        }

        /// <summary>
        /// Recalculate viewport dimensions and re-layout pages (call if layout changes).
        /// </summary>
        public void RefreshLayout()
        {
            RelayoutAndAlign();
        }

        /// <summary>
        /// Get the calculated start page index based on current settings.
        /// </summary>
        public int GetCalculatedStartPageIndex()
        {
            return CalculateStartPageIndex();
        }

        #endregion

        #region Animation

        private void SnapToPage(int targetPage)
        {
            _targetPageIndex = targetPage;
            float targetX = GetPagePositionX(targetPage);

            KillSnapTween();

            _snapTween = DOTween.To(
                () => _content.anchoredPosition.x,
                x => {
                    _tempAnchoredPos = _content.anchoredPosition;
                    _tempAnchoredPos.x = x;
                    _content.anchoredPosition = _tempAnchoredPos;
                },
                targetX,
                _snapDuration
            )
            .SetEase(_snapEase)
            .SetUpdate(true) // Use unscaled time
            .OnComplete(OnSnapComplete);
        }

        private void OnSnapComplete()
        {
            int previousPage = _currentPageIndex;
            _currentPageIndex = _targetPageIndex;

            // Final progress broadcast
            OnPageTransitionProgress?.Invoke(_currentPageIndex, _currentPageIndex, 1f);

            if (previousPage != _currentPageIndex)
            {
                OnPageChanged?.Invoke(_currentPageIndex);
            }

            // Notify listeners that we're no longer busy (unblock navbar, etc.)
            OnBusyStateExit?.Invoke();
        }

        private void KillSnapTween()
        {
            if (_snapTween != null && _snapTween.IsActive())
            {
                _snapTween.Kill();
            }
            _snapTween = null;
        }

        #endregion

        #region Progress Broadcasting

        private void BroadcastTransitionProgress()
        {
            if (_viewportWidth <= 0.0001f)
            {
                return;
            }

            float currentX = _content.anchoredPosition.x;
            float normalizedPos = -currentX / _viewportWidth; // 0 at page 0, 1 at page 1, etc.

            // Clamp for boundary handling
            float minAllowed = GetEffectiveMinAllowedPageIndex();
            float maxAllowed = GetEffectiveMaxAllowedPageIndex();
            normalizedPos = Mathf.Clamp(normalizedPos, minAllowed, maxAllowed);

            int fromPage = Mathf.FloorToInt(normalizedPos);
            int toPage = Mathf.CeilToInt(normalizedPos);

            // Handle edge case where we're exactly on a page
            if (fromPage == toPage)
            {
                OnPageTransitionProgress?.Invoke(fromPage, toPage, 1f);
                return;
            }

            // Calculate progress between pages (0-1)
            float progress = normalizedPos - fromPage;

            OnPageTransitionProgress?.Invoke(fromPage, toPage, progress);
        }

        #endregion

        #region Editor Helpers

#if UNITY_EDITOR
        private void OnValidate()
        {
            _pageCount = Mathf.Max(1, _pageCount);
            _startPageIndex = Mathf.Clamp(_startPageIndex, 0, Mathf.Max(0, _pageCount - 1));
            _snapDuration = Mathf.Max(0.1f, _snapDuration);
            NormalizeAllowedPageRange();
        }
#endif

        #endregion
    }
}
#endif
