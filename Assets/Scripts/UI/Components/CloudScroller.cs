using UnityEngine;
using UnityEngine.UI;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Scrolls cloud images horizontally across the screen in the main menu.
    /// Each cloud entry can have its own speed, Y position and scale.
    /// When a cloud exits the left side, it wraps back to the right.
    /// </summary>
    public class CloudScroller : MonoBehaviour
    {
        [System.Serializable]
        public class CloudEntry
        {
            [Tooltip("The RectTransform of the cloud Image")]
            public RectTransform rectTransform;

            [Tooltip("Horizontal scroll speed in pixels per second")]
            public float speed = 30f;
        }

        [Header("Clouds")]
        [SerializeField] private CloudEntry[] _clouds;

        [Header("Bounds")]
        [Tooltip("Extra padding beyond screen edge before cloud wraps")]
        [SerializeField] private float _offScreenPadding = 50f;

        private RectTransform _canvasRect;
        private float _canvasHalfWidth;

        private void Start()
        {
            // Find parent Canvas RectTransform for width reference
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                _canvasRect = canvas.GetComponent<RectTransform>();
                _canvasHalfWidth = _canvasRect.rect.width * 0.5f;
            }
        }

        private void Update()
        {
            if (_canvasRect == null || _clouds == null) return;

            // Recalculate in case of resolution change
            _canvasHalfWidth = _canvasRect.rect.width * 0.5f;

            for (int i = 0; i < _clouds.Length; i++)
            {
                var cloud = _clouds[i];
                if (cloud.rectTransform == null) continue;

                var pos = cloud.rectTransform.anchoredPosition;
                pos.x -= cloud.speed * Time.deltaTime;

                // Cloud's own width (scaled)
                float cloudHalfWidth = cloud.rectTransform.rect.width
                                       * cloud.rectTransform.localScale.x * 0.5f;

                // If fully exited left side, wrap to right side
                float leftExit = -_canvasHalfWidth - cloudHalfWidth - _offScreenPadding;
                if (pos.x < leftExit)
                {
                    float rightEntry = _canvasHalfWidth + cloudHalfWidth + _offScreenPadding;
                    pos.x = rightEntry;
                }

                cloud.rectTransform.anchoredPosition = pos;
            }
        }
    }
}
