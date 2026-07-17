using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasScaler))]
public class DynamicCanvasScaler : MonoBehaviour
{
    public static event Action ScreenConfigurationChanged;

    [Header("Aspect Ratio Thresholds")]
    [Tooltip("Aspect ratio at or below which match = 1 (full height)")]
    [SerializeField] private float tabletAspectRatio = 1.5f;

    [Tooltip("Aspect ratio at or above which match = 0 (full width)")]
    [SerializeField] private float phoneAspectRatio = 2.0f;

    [Header("Performance")]
    [Tooltip("0 = check every frame. >0 = check this often (seconds). Example: 0.2 checks 5 times/sec.")]
    [SerializeField] private float updateCheckInterval = 0f;

    [Header("Debug")]
    [SerializeField] private bool logDebugInfo = false;

    private CanvasScaler canvasScaler;

    private int lastW;
    private int lastH;
    private Rect lastSafeArea;
    private float lastMatch = -1f;

    private void Awake()
    {
        canvasScaler = GetComponent<CanvasScaler>();
        ForceAdjust();
    }

    private IEnumerator Start()
    {
        yield return null;
        ForceAdjust();

        if (updateCheckInterval > 0f)
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(updateCheckInterval);
                CheckAndAdjustIfNeeded();
            }
        }
    }

    private void Update()
    {
        if (updateCheckInterval > 0f) return; // handled by coroutine
        CheckAndAdjustIfNeeded();
    }

    private void CheckAndAdjustIfNeeded()
    {
        if (Screen.width != lastW || Screen.height != lastH || Screen.safeArea != lastSafeArea)
        {
            ForceAdjust();
        }
    }

    private void ForceAdjust()
    {
        lastW = Screen.width;
        lastH = Screen.height;
        lastSafeArea = Screen.safeArea;
        AdjustMatch();
        ScreenConfigurationChanged?.Invoke();
    }

    private void AdjustMatch()
    {
        if (canvasScaler == null) return;
        if (canvasScaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;

        float w = Screen.width;
        float h = Screen.height;

        // Use long/short to handle portrait/landscape consistently
        float aspect = Mathf.Max(w, h) / Mathf.Min(w, h);

        // aspect <= tabletAspectRatio => match ~ 1 (height)
        // aspect >= phoneAspectRatio  => match ~ 0 (width)
        float match = Mathf.InverseLerp(phoneAspectRatio, tabletAspectRatio, aspect);

        if (!Mathf.Approximately(match, lastMatch))
        {
            canvasScaler.matchWidthOrHeight = match;
            lastMatch = match;

            if (logDebugInfo)
            {
                Debug.Log(
                    $"[DynamicCanvasScaler] Screen:{w}x{h} SafeArea:{Screen.safeArea} " +
                    $"Aspect:{aspect:F2} Match:{match:F2}"
                );
            }
        }
        else if (logDebugInfo)
        {
            Debug.Log(
                $"[DynamicCanvasScaler] (no change) Screen:{w}x{h} SafeArea:{Screen.safeArea} " +
                $"Aspect:{aspect:F2} Match:{match:F2}"
            );
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        canvasScaler = GetComponent<CanvasScaler>();
        ForceAdjust();
    }
#endif
}
