#if DOTWEEN
using DG.Tweening;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 12f, -8.65f);
    [SerializeField] private float smoothSpeed = 12f;
    [SerializeField] private bool followInstantly = false;

    [Header("Zoom")]
    [SerializeField] private bool zoomByScale = true;
    [SerializeField] private bool useRootScaleForZoom = true;
    [SerializeField] private bool useParentScaleForZoom;
    [SerializeField] private bool scaleWithTarget;
    [SerializeField] private float baseZoom = 2.8f;
    [SerializeField, Range(0f, 1f)] private float zoomScaleInfluence = 0.5f;
    [SerializeField] private float minZoomScale = 0.7f;
    [SerializeField] private float maxZoomScale = 2.5f;

    [Header("Intro Zoom Out")]
    [Tooltip("Zoom multiplier at game start. >1 means zoomed out. The camera starts at baseZoom * this value.")]
    [SerializeField] private float introZoomOutMultiplier = 1.35f;

    [Tooltip("Duration of the zoom-in tween when the player first moves.")]
    [SerializeField] private float introZoomInDuration = 1f;

    [Tooltip("Ease curve for the intro zoom-in.")]
    [SerializeField] private Ease introZoomInEase = Ease.InOutSine;

    private float zoom = 2.8f;
    private bool isFollowingObject;
    private float initialTargetScale = 1f;
    private bool _introZoomActive;
    private Tween zoomTween;

    public float BaseZoom => baseZoom;
    public float Zoom => zoom;

    private void Awake()
    {
        zoom = baseZoom;
        if (target != null)
        {
            var scaleSource = GetZoomScaleSource();
            initialTargetScale = Mathf.Approximately(scaleSource.localScale.x, 0f) ? 1f : scaleSource.localScale.x;
            UpdateFollowInstantly();
        }
    }

    private void LateUpdate()
    {
        if (target == null || isFollowingObject)
        {
            return;
        }

        var targetPos = target.position;
        var desired = GetDesiredPosition(targetPos);
        if (followInstantly)
        {
            transform.position = desired;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, desired, Mathf.Clamp01(smoothSpeed * Time.deltaTime));
        }

        // Rotation is set once in UpdateFollowInstantly and never changes during follow

        if (scaleWithTarget && target != null)
        {
            transform.localScale = target.localScale;
        }
    }

    private Vector3 GetDesiredPosition(Vector3 targetPos)
    {
        var scale = 1f;
        if (zoomByScale && target != null)
        {
            var scaleSource = GetZoomScaleSource();
            var currentScale = scaleSource.localScale.x;
            if (!Mathf.Approximately(initialTargetScale, 0f))
            {
                var rawScale = currentScale / initialTargetScale;
                var influenced = Mathf.Lerp(1f, rawScale, zoomScaleInfluence);
                scale = Mathf.Clamp(influenced, minZoomScale, maxZoomScale);
            }
        }

        // Rotate offset based on target's Y rotation
        var rotatedOffset = target != null
            ? Quaternion.Euler(0f, target.eulerAngles.y, 0f) * offset
            : offset;

        return targetPos + rotatedOffset * zoom * scale;
    }

    private Transform GetZoomScaleSource()
    {
        if (target == null)
        {
            return transform;
        }

        if (useRootScaleForZoom && target.root != null)
        {
            return target.root;
        }

        if (useParentScaleForZoom && target.parent != null)
        {
            return target.parent;
        }

        return target;
    }

    public void UpdateFollowInstantly()
    {
        if (target == null)
        {
            return;
        }

        transform.position = GetDesiredPosition(target.position);
        transform.LookAt(target);
    }

    public void UpdateZoom(float value)
    {
        zoom = value;
    }

    public void SetZoom(float value)
    {
        UpdateZoom(value);
    }

    public void UpdateZoomTween(float targetZoom, float duration)
    {
        zoomTween?.Kill();
        zoom = targetZoom;
        zoomTween = DOTween.To(() => zoom, v => zoom = v, targetZoom, duration)
            .SetEase(Ease.OutSine);
    }

    // ── Intro Zoom Out ──────────────────────────────────────────────

    /// <summary>
    /// Whether the intro zoom-out is currently active (waiting for first movement).
    /// </summary>
    public bool IsIntroZoomActive => _introZoomActive;

    /// <summary>
    /// Immediately sets the camera to the zoomed-out intro position.
    /// Call once when the level starts (before player can move).
    /// </summary>
    public void BeginIntroZoom()
    {
        if (introZoomOutMultiplier <= 1f) return;

        _introZoomActive = true;
        zoomTween?.Kill();
        zoom = baseZoom * introZoomOutMultiplier;
        UpdateFollowInstantly();
    }

    /// <summary>
    /// Smoothly zooms back to the default baseZoom.
    /// Call when the player's first movement is detected.
    /// </summary>
    public void EndIntroZoom()
    {
        if (!_introZoomActive) return;

        _introZoomActive = false;
        zoomTween?.Kill();
        zoomTween = DOTween.To(() => zoom, v => zoom = v, baseZoom, introZoomInDuration)
            .SetEase(introZoomInEase);
    }

    public void SetLookAtState(bool active)
    {
        isFollowingObject = active;
    }

    /// <summary>
    /// Sets the target transform for the camera to follow.
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null)
        {
            var scaleSource = GetZoomScaleSource();
            initialTargetScale = Mathf.Approximately(scaleSource.localScale.x, 0f) ? 1f : scaleSource.localScale.x;
        }
    }

    /// <summary>
    /// Gets the current target transform.
    /// </summary>
    public Transform Target => target;

    [ContextMenu("Apply HoleGame Defaults")]
    private void ApplyHoleGameDefaults()
    {
        offset = new Vector3(0f, 12f, -8.65f);
        smoothSpeed = 12f;
        followInstantly = false;
        baseZoom = 2.8f;
        zoomByScale = true;
        useRootScaleForZoom = true;
        useParentScaleForZoom = false;
        scaleWithTarget = false;
        zoomScaleInfluence = 0.5f;
        minZoomScale = 0.7f;
        maxZoomScale = 2.5f;
        zoom = baseZoom;
    }
}
#endif
