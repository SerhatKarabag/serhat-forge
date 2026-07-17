using UnityEngine;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Anchors a RectTransform to the bottom of the screen's safe area.
    /// Attach to a parent object whose children should sit above the home indicator/navigation bar.
    /// Runs once on Awake — no per-frame overhead.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaBottom : MonoBehaviour
    {
        private void Awake()
        {
            var rect = GetComponent<RectTransform>();
            var safeArea = Screen.safeArea;

            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            if (canvas == null) return;

            var canvasH = canvas.GetComponent<RectTransform>().rect.height;
            var screenH = (float)Screen.height;

            var bottomInset = safeArea.y;
            var bottomInsetCanvas = bottomInset / screenH * canvasH;

            rect.anchorMin = new Vector2(rect.anchorMin.x, 0f);
            rect.offsetMin = new Vector2(rect.offsetMin.x, bottomInsetCanvas);
        }
    }
}
