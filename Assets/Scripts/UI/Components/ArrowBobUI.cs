using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class ArrowBobUI : MonoBehaviour
{
    [Header("Motion")]
    [SerializeField, Min(0f)] private float _amplitude = 14f;
    [SerializeField, Min(0.01f)] private float _frequency = 1.2f;
    [SerializeField] private bool _useUnscaledTime = true;
    [SerializeField] private Vector2 _direction = Vector2.up;

    private RectTransform _rectTransform;
    private Vector2 _baseAnchoredPosition;
    private Vector2 _normalizedDirection;

    private void Awake()
    {
        _rectTransform = (RectTransform)transform;
        _normalizedDirection = _direction.sqrMagnitude > 0.0001f ? _direction.normalized : Vector2.up;
        _baseAnchoredPosition = _rectTransform.anchoredPosition;
    }

    private void OnEnable()
    {
        _baseAnchoredPosition = _rectTransform.anchoredPosition;
    }

    private void Update()
    {
        float time = _useUnscaledTime ? Time.unscaledTime : Time.time;
        float offset = Mathf.Sin(time * _frequency * 2f * Mathf.PI) * _amplitude;
        _rectTransform.anchoredPosition = _baseAnchoredPosition + (_normalizedDirection * offset);
    }
}
