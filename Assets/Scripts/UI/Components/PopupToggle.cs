#if DOTWEEN
using System;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Opens/closes a popup with casual game animations.
    /// Open: scale bounce (0 → 1 overshoot) + fade in.
    /// Close: scale shrink (1 → 0) + fade out, then deactivates.
    /// Disables specified ScrollRects while popup is open to prevent swipe.
    /// </summary>
    public class PopupToggle : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject _popup;
        [SerializeField] private Transform _popupBody;
        [SerializeField] private CanvasGroup _popupBodyCanvasGroup;

        [Header("Background Block")]
        [Tooltip("ScrollRects to disable while popup is open (prevents swipe)")]
        [SerializeField] private ScrollRect[] _scrollRectsToDisable;

        [Tooltip("GameObjects to hide (SetActive(false)) while the popup is open. Restored (SetActive(true)) when the popup finishes its close animation.")]
        [SerializeField] private GameObject[] _objectsToHideWhileOpen;

        [Tooltip("CanvasGroups to fade out (alpha=0, interactable/blocksRaycasts=false) while the popup is open. " +
                 "Use this instead of _objectsToHideWhileOpen for objects whose visual must stay up-to-date while hidden " +
                 "(e.g. a level button that switches to a 'hard' variant — SetActive(false) makes it miss state updates and flicker on re-show).")]
        [SerializeField] private CanvasGroup[] _canvasGroupsToFadeWhileOpen;

        [Header("Animation")]
        [SerializeField] private float _openDuration = 0.35f;
        [SerializeField] private float _closeDuration = 0.2f;
        [SerializeField] private float _openTargetScale = 1f;
        [SerializeField] private Ease _openEase = Ease.OutBack;
        [SerializeField] private Ease _closeEase = Ease.InBack;

        /// <summary>
        /// Fired when the open animation finishes.
        /// </summary>
        public event Action OnOpenComplete;

        /// <summary>
        /// Fired after the close animation completes and the popup is deactivated.
        /// </summary>
        public event Action OnCloseComplete;

        private Tween _activeTween;
        private Tween _activeFadeTween;
        private bool _isRegisteredOpen;

        /// <summary>
        /// Global count of currently open popups. O(1) read — updated by Open/Close transitions.
        /// Use instead of scene scans when coordinating popups.
        /// </summary>
        private static int _openCount;
        public static int OpenCount => _openCount;

        /// <summary>
        /// Fires once per popup-close transition (any popup, any part of the app). Subscribers receive
        /// the just-closed toggle and can decide if they want to attempt a deferred open.
        /// O(1) subscription, no scene scans.
        /// </summary>
        public static event Action<PopupToggle> OnAnyPopupClosed;

        public GameObject PopupObject => _popup;

        public void Open()
        {
            if (_popup == null || _popupBody == null) return;

            KillActiveTweens();

            _popup.SetActive(true);
            MarkOpened();
            SetScrollRectsEnabled(false);
            SetObjectsHiddenWhileOpen(true);
            SetCanvasGroupsFadedWhileOpen(true);

            _popupBody.localScale = Vector3.zero;

            if (_popupBodyCanvasGroup != null)
            {
                _popupBodyCanvasGroup.alpha = 0f;
                _activeFadeTween = _popupBodyCanvasGroup.DOFade(1f, _openDuration * 0.5f).SetUpdate(true);
            }

            _activeTween = _popupBody
                .DOScale(Vector3.one * _openTargetScale, _openDuration)
                .SetEase(_openEase)
                .SetUpdate(true)
                .OnComplete(() => OnOpenComplete?.Invoke());
        }

        public void Close()
        {
            if (_popup == null || _popupBody == null) return;

            KillActiveTweens();

            if (_popupBodyCanvasGroup != null)
                _activeFadeTween = _popupBodyCanvasGroup.DOFade(0f, _closeDuration).SetUpdate(true);

            _activeTween = _popupBody
                .DOScale(Vector3.zero, _closeDuration)
                .SetEase(_closeEase)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _popup.SetActive(false);
                    MarkClosed();
                    SetScrollRectsEnabled(true);
                    SetObjectsHiddenWhileOpen(false);
                    SetCanvasGroupsFadedWhileOpen(false);
                    OnCloseComplete?.Invoke();
                });
        }

        public void Toggle()
        {
            if (_popup == null) return;

            if (_popup.activeSelf)
                Close();
            else
                Open();
        }

        public void HideBodyForOverlay()
        {
            if (_popup == null || _popupBody == null || !_popup.activeSelf)
                return;

            KillActiveTweens();

            if (_popupBodyCanvasGroup != null)
                _activeFadeTween = _popupBodyCanvasGroup.DOFade(0f, _closeDuration).SetUpdate(true);

            _activeTween = _popupBody
                .DOScale(Vector3.zero, _closeDuration)
                .SetEase(_closeEase)
                .SetUpdate(true);
        }

        public void ShowBodyFromOverlay()
        {
            if (_popup == null || _popupBody == null || !_popup.activeSelf)
                return;

            KillActiveTweens();

            _popupBody.localScale = Vector3.zero;

            if (_popupBodyCanvasGroup != null)
            {
                _popupBodyCanvasGroup.alpha = 0f;
                _activeFadeTween = _popupBodyCanvasGroup.DOFade(1f, _openDuration * 0.5f).SetUpdate(true);
            }

            _activeTween = _popupBody
                .DOScale(Vector3.one * _openTargetScale, _openDuration)
                .SetEase(_openEase)
                .SetUpdate(true);
        }

        private void SetScrollRectsEnabled(bool enabled)
        {
            if (_scrollRectsToDisable == null) return;

            for (int i = 0; i < _scrollRectsToDisable.Length; i++)
            {
                if (_scrollRectsToDisable[i] != null)
                    _scrollRectsToDisable[i].enabled = enabled;
            }
        }

        private void SetObjectsHiddenWhileOpen(bool hidden)
        {
            if (_objectsToHideWhileOpen == null) return;

            for (int i = 0; i < _objectsToHideWhileOpen.Length; i++)
            {
                var go = _objectsToHideWhileOpen[i];
                if (go != null)
                    go.SetActive(!hidden);
            }
        }

        private void SetCanvasGroupsFadedWhileOpen(bool faded)
        {
            if (_canvasGroupsToFadeWhileOpen == null) return;

            for (int i = 0; i < _canvasGroupsToFadeWhileOpen.Length; i++)
            {
                var cg = _canvasGroupsToFadeWhileOpen[i];
                if (cg == null) continue;

                cg.alpha = faded ? 0f : 1f;
                cg.interactable = !faded;
                cg.blocksRaycasts = !faded;
            }
        }

        private void OnDisable()
        {
            // If the popup GO was turned off externally (scene unload, parent disabled, etc.)
            // without the close tween running to completion, clean up the open-count to prevent leaks.
            MarkClosed();
        }

        private void OnDestroy()
        {
            KillActiveTweens();
            MarkClosed();
        }

        private void MarkOpened()
        {
            if (_isRegisteredOpen)
            {
                return;
            }

            _isRegisteredOpen = true;
            _openCount++;
        }

        private void MarkClosed()
        {
            if (!_isRegisteredOpen)
            {
                return;
            }

            _isRegisteredOpen = false;
            if (_openCount > 0)
            {
                _openCount--;
            }

            OnAnyPopupClosed?.Invoke(this);
        }

        private void KillActiveTweens()
        {
            _activeTween?.Kill();
            _activeTween = null;

            _activeFadeTween?.Kill();
            _activeFadeTween = null;
        }
    }
}
#endif
