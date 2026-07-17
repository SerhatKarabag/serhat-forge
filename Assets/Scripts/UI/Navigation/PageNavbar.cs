#if DOTWEEN
using System;
using System.Collections;
using UnityEngine;
using Serhat.Forge.Audio;
using Zenject;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Serhat.Forge.UI.Navigation
{
    /// <summary>
    /// Manages the bottom navigation bar.
    /// Syncs navbar visual state with SwipePageController and handles item clicks.
    /// Uses CanvasGroup to block all interaction during page drag/snap.
    /// </summary>
    public class PageNavbar : MonoBehaviour
    {
        [Serializable]
        private struct LockedPageConfig
        {
            [Min(0)]
            public int PageIndex;
            public LockedNavbarTooltip Tooltip;
        }

        #region Inspector Fields

        [Header("References")]
        [SerializeField] private SwipePageController _pageController;
        [SerializeField] private NavbarItem[] _navbarItems;

        [Header("Interaction Blocking")]
        [Tooltip("CanvasGroup used to block raycasts during drag/snap. Required for reliable blocking.")]
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("Settings")]
        [Tooltip("Enable smooth cross-fade during page transitions")]
        [SerializeField] private bool _smoothTransitions = true;

        [Tooltip("If set to a non-negative value, this page index is treated as the shop page for analytics.")]
        [SerializeField] private int _shopPageIndex = -1;

        [Tooltip("If set to a non-negative value, this page index is treated as the leaderboard page for analytics.")]
        [SerializeField] private int _leaderboardPageIndex = -1;

        [Header("Selection Indicator")]
        [Tooltip("Image that moves behind the active navbar item")]
        [SerializeField] private RectTransform _selectionIndicator;

        [Header("Locked Pages")]
        [Tooltip("Assign locked page indices and their tooltip references from Inspector.")]
        [SerializeField] private LockedPageConfig[] _lockedPages;

        #endregion

        #region Private Fields

        [Inject] private ISfxService _sfxService;

        /// <summary>
        /// Hook called when a navbar page is opened. Wire to your analytics from the scene.
        /// Args: (pageIndex, navbarItemName).
        /// </summary>
        public static System.Action<int, string> OnPageOpened;
        private int _currentActiveIndex = -1;
        private bool[] _lockedFlagsByPage;
        private LockedNavbarTooltip[] _lockedTooltipsByPage;
        private LockedNavbarTooltip _activeLockedTooltip;
        private int _activeLockedTooltipPageIndex = -1;
        private int _tooltipOpenedFrame = -1;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            InitializeNavbarItems();
            InitializeLockedPages();
            ValidateCanvasGroup();
        }

        private void OnEnable()
        {
            if (_pageController != null)
            {
                _pageController.OnPageTransitionProgress += HandleTransitionProgress;
                _pageController.OnPageChanged += HandlePageChanged;
                _pageController.OnBusyStateEnter += HandleBusyEnter;
                _pageController.OnBusyStateExit += HandleBusyExit;
            }
        }

        private void OnDisable()
        {
            if (_pageController != null)
            {
                _pageController.OnPageTransitionProgress -= HandleTransitionProgress;
                _pageController.OnPageChanged -= HandlePageChanged;
                _pageController.OnBusyStateEnter -= HandleBusyEnter;
                _pageController.OnBusyStateExit -= HandleBusyExit;
            }

            HideLockedTooltipsExcept(-1);
        }

        private void Update()
        {
            if (_activeLockedTooltip == null)
            {
                return;
            }

            if (!TryGetPointerDownPosition(out Vector2 screenPoint))
            {
                return;
            }

            if (Time.frameCount == _tooltipOpenedFrame)
            {
                return;
            }

            if (_activeLockedTooltip.ContainsScreenPoint(screenPoint))
            {
                return;
            }

            HideActiveLockedTooltipAnimated();
        }

        private void Start()
        {
            // Set initial state after page controller has initialized
            if (_pageController != null)
            {
                SetActiveItemImmediate(_pageController.CurrentPageIndex);
            }

            // Ensure navbar starts interactable
            SetInteractable(true);

            // Initialize selection indicator position after layout is calculated
            StartCoroutine(InitializeSelectionIndicatorDelayed());
        }

        private IEnumerator InitializeSelectionIndicatorDelayed()
        {
            // Wait for end of frame to ensure layout is calculated
            yield return new WaitForEndOfFrame();

            if (_pageController != null)
            {
                SetSelectionIndicatorImmediate(_pageController.CurrentPageIndex);
            }
        }

        #endregion

        #region Initialization

        private void InitializeNavbarItems()
        {
            if (_navbarItems == null)
                return;

            for (int i = 0; i < _navbarItems.Length; i++)
            {
                if (_navbarItems[i] == null)
                    continue;

                _navbarItems[i].Initialize(i);
                _navbarItems[i].OnItemClicked += HandleNavbarItemClicked;
            }
        }

        private void InitializeLockedPages()
        {
            int mapSize = _navbarItems != null ? _navbarItems.Length : 0;
            if (_pageController != null)
            {
                mapSize = Mathf.Max(mapSize, _pageController.PageCount);
            }

            if (mapSize <= 0)
            {
                _lockedFlagsByPage = Array.Empty<bool>();
                _lockedTooltipsByPage = Array.Empty<LockedNavbarTooltip>();
                ApplySwipeRangeLock();
                return;
            }

            if (_lockedFlagsByPage == null || _lockedFlagsByPage.Length != mapSize)
            {
                _lockedFlagsByPage = new bool[mapSize];
            }
            else
            {
                Array.Clear(_lockedFlagsByPage, 0, _lockedFlagsByPage.Length);
            }

            if (_lockedTooltipsByPage == null || _lockedTooltipsByPage.Length != mapSize)
            {
                _lockedTooltipsByPage = new LockedNavbarTooltip[mapSize];
            }
            else
            {
                Array.Clear(_lockedTooltipsByPage, 0, _lockedTooltipsByPage.Length);
            }

            if (_lockedPages != null)
            {
                for (int i = 0; i < _lockedPages.Length; i++)
                {
                    int pageIndex = _lockedPages[i].PageIndex;
                    if (pageIndex < 0 || pageIndex >= mapSize)
                    {
                        continue;
                    }

                    _lockedFlagsByPage[pageIndex] = true;
                    _lockedTooltipsByPage[pageIndex] = _lockedPages[i].Tooltip;

                    if (_lockedPages[i].Tooltip != null)
                    {
                        _lockedPages[i].Tooltip.HideImmediate();
                    }
                }
            }

            ApplySwipeRangeLock();
        }

        private void ApplySwipeRangeLock()
        {
            if (_pageController == null || _pageController.PageCount <= 0)
            {
                return;
            }

            int minAllowed = 0;
            int maxAllowed = _pageController.PageCount - 1;

            while (minAllowed <= maxAllowed && IsPageLocked(minAllowed))
            {
                minAllowed++;
            }

            while (maxAllowed >= minAllowed && IsPageLocked(maxAllowed))
            {
                maxAllowed--;
            }

            if (minAllowed > maxAllowed)
            {
                _pageController.ClearAllowedPageRange();
                return;
            }

            bool hasRestriction = minAllowed > 0 || maxAllowed < _pageController.PageCount - 1;
            if (hasRestriction)
            {
                _pageController.SetAllowedPageRange(minAllowed, maxAllowed);
            }
            else
            {
                _pageController.ClearAllowedPageRange();
            }
        }

        private bool IsPageLocked(int pageIndex)
        {
            return _lockedFlagsByPage != null &&
                   pageIndex >= 0 &&
                   pageIndex < _lockedFlagsByPage.Length &&
                   _lockedFlagsByPage[pageIndex];
        }

        private bool TryShowLockedTooltip(int pageIndex)
        {
            if (!IsPageLocked(pageIndex))
            {
                return false;
            }

            HideLockedTooltipsExcept(pageIndex);

            LockedNavbarTooltip tooltip = _lockedTooltipsByPage[pageIndex];
            if (tooltip != null)
            {
                tooltip.ShowFrom(GetNavbarItemRectByPageIndex(pageIndex));
                _activeLockedTooltip = tooltip;
                _activeLockedTooltipPageIndex = pageIndex;
                _tooltipOpenedFrame = Time.frameCount;
            }
            else
            {
                ClearActiveLockedTooltipState();
                Debug.LogWarning($"[PageNavbar] Locked page '{pageIndex}' has no tooltip reference.", this);
            }

            return true;
        }

        private RectTransform GetNavbarItemRectByPageIndex(int pageIndex)
        {
            if (_navbarItems == null)
            {
                return null;
            }

            for (int i = 0; i < _navbarItems.Length; i++)
            {
                NavbarItem item = _navbarItems[i];
                if (item == null || item.PageIndex != pageIndex)
                {
                    continue;
                }

                return item.IconTransform;
            }

            return null;
        }

        private void HideLockedTooltipsExcept(int pageIndexToKeep)
        {
            if (_lockedTooltipsByPage == null)
            {
                if (pageIndexToKeep < 0)
                {
                    ClearActiveLockedTooltipState();
                }
                return;
            }

            for (int i = 0; i < _lockedTooltipsByPage.Length; i++)
            {
                if (i == pageIndexToKeep)
                {
                    continue;
                }

                LockedNavbarTooltip tooltip = _lockedTooltipsByPage[i];
                if (tooltip != null)
                {
                    tooltip.HideImmediate();
                }

                if (_activeLockedTooltipPageIndex == i)
                {
                    ClearActiveLockedTooltipState();
                }
            }

            if (pageIndexToKeep < 0)
            {
                ClearActiveLockedTooltipState();
            }
        }

        private void HideActiveLockedTooltipAnimated()
        {
            if (_activeLockedTooltip != null)
            {
                _activeLockedTooltip.Hide();
            }

            ClearActiveLockedTooltipState();
        }

        private void ClearActiveLockedTooltipState()
        {
            _activeLockedTooltip = null;
            _activeLockedTooltipPageIndex = -1;
            _tooltipOpenedFrame = -1;
        }

        private static bool TryGetPointerDownPosition(out Vector2 screenPoint)
        {
#if ENABLE_INPUT_SYSTEM
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    if (touches[i].press.wasPressedThisFrame)
                    {
                        screenPoint = touches[i].position.ReadValue();
                        return true;
                    }
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPoint = mouse.position.ReadValue();
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    screenPoint = touch.position;
                    return true;
                }
            }

            if (Input.GetMouseButtonDown(0))
            {
                screenPoint = Input.mousePosition;
                return true;
            }
#endif

            screenPoint = default;
            return false;
        }

        private void ValidateCanvasGroup()
        {
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();

                if (_canvasGroup == null)
                {
                    Debug.LogWarning($"[PageNavbar] No CanvasGroup assigned or found on {gameObject.name}. " +
                        "Navbar blocking during drag/snap will not work reliably. " +
                        "Add a CanvasGroup component to the Navbar GameObject and assign it.", this);
                }
            }
        }

        #endregion

        #region Busy State Handlers

        private void HandleBusyEnter()
        {
            SetInteractable(false);
        }

        private void HandleBusyExit()
        {
            SetInteractable(true);
        }

        private void SetInteractable(bool interactable)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = interactable;
                _canvasGroup.blocksRaycasts = interactable;
            }
        }

        #endregion

        #region Event Handlers

        private void HandleNavbarItemClicked(NavbarItem item)
        {
            if (_pageController == null || item == null)
                return;

            if (TryShowLockedTooltip(item.PageIndex))
            {
                _sfxService?.PlayButtonClick();
                return;
            }

            HideLockedTooltipsExcept(-1);

            if (item.PageIndex == _pageController.CurrentPageIndex)
                return;

            _sfxService?.PlayButtonClick();

            OnPageOpened?.Invoke(item.PageIndex, item != null ? item.name : string.Empty);

            _pageController.GoToPage(item.PageIndex);
        }

        private bool IsShopNavbarItem(NavbarItem item)
        {
            if (_shopPageIndex >= 0)
            {
                return item.PageIndex == _shopPageIndex;
            }

            return item.name.IndexOf("shop", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsLeaderboardPageIndex(int pageIndex)
        {
            if (_leaderboardPageIndex >= 0)
            {
                return pageIndex == _leaderboardPageIndex;
            }

            if (_navbarItems == null || pageIndex < 0 || pageIndex >= _navbarItems.Length)
            {
                return false;
            }

            var item = _navbarItems[pageIndex];
            if (item == null)
            {
                return false;
            }

            return item.name.IndexOf("leader", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void HandleTransitionProgress(int fromPage, int toPage, float progress)
        {
            if (!_smoothTransitions || _navbarItems == null)
                return;

            if (_pageController != null && _pageController.IsAllowedPageRangeEnabled)
            {
                int minAllowed = _pageController.MinAllowedPageIndex;
                int maxAllowed = _pageController.MaxAllowedPageIndex;
                fromPage = Mathf.Clamp(fromPage, minAllowed, maxAllowed);
                toPage = Mathf.Clamp(toPage, minAllowed, maxAllowed);
                if (fromPage == toPage)
                {
                    progress = 1f;
                }
            }

            // Update visual state for all items based on transition
            for (int i = 0; i < _navbarItems.Length; i++)
            {
                if (_navbarItems[i] == null)
                    continue;

                float itemProgress;

                if (i == fromPage && i == toPage)
                {
                    // We're on this page (no transition happening)
                    itemProgress = 1f;
                }
                else if (i == fromPage)
                {
                    // Transitioning away from this page
                    itemProgress = 1f - progress;
                }
                else if (i == toPage)
                {
                    // Transitioning to this page
                    itemProgress = progress;
                }
                else
                {
                    // Not involved in this transition
                    itemProgress = 0f;
                }

                _navbarItems[i].SetVisualProgress(itemProgress);
            }

            // Update selection indicator position
            UpdateSelectionIndicatorPosition(fromPage, toPage, progress);
        }

        private void HandlePageChanged(int newPageIndex)
        {
            HideLockedTooltipsExcept(-1);
            _currentActiveIndex = newPageIndex;

            // Ensure clean final state (in case smooth transitions are disabled)
            if (!_smoothTransitions)
            {
                SetActiveItemImmediate(newPageIndex);
            }

            // Ensure indicator is at final position
            SetSelectionIndicatorImmediate(newPageIndex);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Manually set the active navbar item without animation.
        /// </summary>
        public void SetActiveItemImmediate(int index)
        {
            if (_navbarItems == null)
                return;

            _currentActiveIndex = index;

            for (int i = 0; i < _navbarItems.Length; i++)
            {
                if (_navbarItems[i] != null)
                {
                    _navbarItems[i].SetActiveState(i == index);
                }
            }

            // Set indicator position immediately
            SetSelectionIndicatorImmediate(index);
        }

        /// <summary>
        /// Get the NavbarItem at a specific index.
        /// </summary>
        public NavbarItem GetItem(int index)
        {
            if (_navbarItems == null || index < 0 || index >= _navbarItems.Length)
                return null;

            return _navbarItems[index];
        }

        /// <summary>
        /// Dynamically update navbar items array.
        /// </summary>
        public void SetNavbarItems(NavbarItem[] items)
        {
            // Unsubscribe from old items
            if (_navbarItems != null)
            {
                for (int i = 0; i < _navbarItems.Length; i++)
                {
                    if (_navbarItems[i] != null)
                    {
                        _navbarItems[i].OnItemClicked -= HandleNavbarItemClicked;
                    }
                }
            }

            _navbarItems = items;
            InitializeNavbarItems();
            InitializeLockedPages();

            if (_pageController != null)
            {
                SetActiveItemImmediate(_pageController.CurrentPageIndex);
            }
        }

        #endregion

        #region Selection Indicator

        /// <summary>
        /// Update selection indicator position based on transition progress.
        /// </summary>
        private void UpdateSelectionIndicatorPosition(int fromPage, int toPage, float progress)
        {
            if (_selectionIndicator == null || _navbarItems == null)
                return;

            if (fromPage < 0 || fromPage >= _navbarItems.Length ||
                toPage < 0 || toPage >= _navbarItems.Length)
                return;

            var fromItem = _navbarItems[fromPage];
            var toItem = _navbarItems[toPage];

            if (fromItem == null || toItem == null)
                return;

            // Get RectTransforms of the navbar items
            RectTransform fromRect = fromItem.GetComponent<RectTransform>();
            RectTransform toRect = toItem.GetComponent<RectTransform>();

            if (fromRect == null || toRect == null)
                return;

            // Get world positions and convert to indicator's local space
            Vector3 fromWorldPos = fromRect.position;
            Vector3 toWorldPos = toRect.position;
            Vector3 lerpedWorldPos = Vector3.Lerp(fromWorldPos, toWorldPos, progress);

            // Convert to indicator's parent local space
            if (_selectionIndicator.parent != null)
            {
                Vector3 localPos = _selectionIndicator.parent.InverseTransformPoint(lerpedWorldPos);
                _selectionIndicator.localPosition = new Vector3(localPos.x, _selectionIndicator.localPosition.y, localPos.z);
            }
            else
            {
                _selectionIndicator.position = new Vector3(lerpedWorldPos.x, _selectionIndicator.position.y, lerpedWorldPos.z);
            }
        }

        /// <summary>
        /// Set selection indicator position immediately without animation.
        /// </summary>
        private void SetSelectionIndicatorImmediate(int index)
        {
            if (_selectionIndicator == null || _navbarItems == null)
                return;

            if (index < 0 || index >= _navbarItems.Length)
                return;

            var item = _navbarItems[index];
            if (item == null)
                return;

            RectTransform itemRect = item.GetComponent<RectTransform>();
            if (itemRect == null)
                return;

            // Get world position and convert to indicator's local space
            Vector3 worldPos = itemRect.position;

            if (_selectionIndicator.parent != null)
            {
                Vector3 localPos = _selectionIndicator.parent.InverseTransformPoint(worldPos);
                _selectionIndicator.localPosition = new Vector3(localPos.x, _selectionIndicator.localPosition.y, localPos.z);
            }
            else
            {
                _selectionIndicator.position = new Vector3(worldPos.x, _selectionIndicator.position.y, worldPos.z);
            }
        }

        #endregion

        #region Editor Helpers

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_lockedPages == null)
            {
                return;
            }

            for (int i = 0; i < _lockedPages.Length; i++)
            {
                if (_lockedPages[i].PageIndex < 0)
                {
                    _lockedPages[i].PageIndex = 0;
                }
            }
        }

        [ContextMenu("Auto-find NavbarItems in children")]
        private void AutoFindNavbarItems()
        {
            _navbarItems = GetComponentsInChildren<NavbarItem>();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Auto-add CanvasGroup")]
        private void AutoAddCanvasGroup()
        {
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                {
                    _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif

        #endregion
    }
}
#endif
