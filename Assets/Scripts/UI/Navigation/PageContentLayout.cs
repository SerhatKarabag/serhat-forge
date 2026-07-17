#if DOTWEEN
using UnityEngine;

namespace Serhat.Forge.UI.Navigation
{
    /// <summary>
    /// Helper component that ensures child pages are correctly sized to fill the viewport.
    /// Attach to the Content RectTransform of the ScrollRect.
    /// </summary>
    [ExecuteAlways]
    public class PageContentLayout : MonoBehaviour
    {
        #region Inspector Fields

        [Header("References")]
        [SerializeField] private RectTransform _viewport;

        [Header("Settings")]
        [Tooltip("Automatically layout children on start")]
        [SerializeField] private bool _layoutOnStart = true;

        #endregion

        #region Private Fields

        private RectTransform _rectTransform;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
        }

        private void Start()
        {
            if (_layoutOnStart && Application.isPlaying)
            {
                LayoutPages();
            }
        }

#if UNITY_EDITOR
        private void Update()
        {
            // In editor, continuously update layout for easy setup
            if (!Application.isPlaying)
            {
                LayoutPages();
            }
        }
#endif

        #endregion

        #region Public API

        /// <summary>
        /// Manually trigger page layout.
        /// </summary>
        [ContextMenu("Layout Pages")]
        public void LayoutPages()
        {
            if (_viewport == null || _rectTransform == null)
                return;

            float viewportWidth = _viewport.rect.width;
            float viewportHeight = _viewport.rect.height;

            if (viewportWidth <= 0 || viewportHeight <= 0)
                return;

            int pageCount = _rectTransform.childCount;

            // Size content to fit all pages horizontally
            _rectTransform.sizeDelta = new Vector2(viewportWidth * pageCount, viewportHeight);

            // Position and size each child page
            for (int i = 0; i < pageCount; i++)
            {
                RectTransform page = _rectTransform.GetChild(i) as RectTransform;
                if (page == null)
                    continue;

                // Set anchors to top-left (we'll position manually)
                page.anchorMin = new Vector2(0f, 0f);
                page.anchorMax = new Vector2(0f, 1f);
                page.pivot = new Vector2(0f, 0.5f);

                // Size to viewport dimensions
                page.sizeDelta = new Vector2(viewportWidth, 0f); // Height is stretched via anchors

                // Position horizontally (page 0 at x=0, page 1 at x=viewportWidth, etc.)
                page.anchoredPosition = new Vector2(i * viewportWidth, 0f);

            }
        }

        /// <summary>
        /// Set viewport reference programmatically.
        /// </summary>
        public void SetViewport(RectTransform viewport)
        {
            _viewport = viewport;
        }

        #endregion

        #region Editor Helpers

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_rectTransform == null)
                _rectTransform = GetComponent<RectTransform>();
        }

        [ContextMenu("Auto-find Viewport")]
        private void AutoFindViewport()
        {
            Transform parent = transform.parent;
            if (parent != null)
            {
                _viewport = parent.GetComponent<RectTransform>();
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif

        #endregion
    }
}
#endif
