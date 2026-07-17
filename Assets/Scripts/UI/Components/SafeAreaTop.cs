using UnityEngine;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Anchors a RectTransform to the top of the screen's safe area.
    /// Attach to a parent object whose children should sit just below the notch/cutout.
    /// Runs once on Awake — no per-frame overhead.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaTop : MonoBehaviour
    {
        private void Awake()
        {
            var rect = GetComponent<RectTransform>();
            var safeArea = Screen.safeArea;

            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            if (canvas == null) return;

            var canvasH = canvas.GetComponent<RectTransform>().rect.height;
            var screenH = (float)Screen.height;

            var topInset = screenH - (safeArea.y + safeArea.height);
            var topInsetCanvas = topInset / screenH * canvasH;

            rect.anchorMax = new Vector2(rect.anchorMax.x, 1f);
            rect.offsetMax = new Vector2(rect.offsetMax.x, -topInsetCanvas);
        }
    }
}
