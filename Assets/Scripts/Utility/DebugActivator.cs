using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// Invokes a configurable debug action after a number of taps inside a time window.
/// SRDebugger integration is optional and compiled only when SRDEBUGGER is defined.
/// </summary>
public sealed class DebugActivator : MonoBehaviour, IPointerClickHandler
{
    [FormerlySerializedAs("requiredTaps")]
    [SerializeField, Min(1)] private int _requiredTaps = 10;

    [FormerlySerializedAs("timeWindow")]
    [SerializeField, Min(0.01f)] private float _timeWindow = 10f;

    [SerializeField] private UnityEvent _onActivated = new UnityEvent();

    private int _tapCount;
    private float _remainingTime;

#if SRDEBUGGER
    private bool _isDebugLoaded;
#endif

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_tapCount == 0)
        {
            _remainingTime = _timeWindow;
        }

        _tapCount++;

        if (_tapCount >= _requiredTaps)
        {
            Activate();
            ResetCounter();
        }
    }

    private void Update()
    {
        if (_remainingTime <= 0f)
            return;

        _remainingTime -= Time.unscaledDeltaTime;
        if (_remainingTime <= 0f)
            ResetCounter();
    }

    private void Activate()
    {
#if SRDEBUGGER
        if (!_isDebugLoaded)
        {
            SRDebug.Init();
            _isDebugLoaded = true;
        }

        SRDebug.Instance.ShowDebugPanel();
#endif

        _onActivated?.Invoke();
    }

    private void ResetCounter()
    {
        _tapCount = 0;
        _remainingTime = 0f;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _requiredTaps = Mathf.Max(1, _requiredTaps);
        _timeWindow = Mathf.Max(0.01f, _timeWindow);
    }
#endif
}
