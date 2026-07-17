#if DOTWEEN
using DG.Tweening;
using UnityEngine;

namespace Serhat.Forge.UI.Components
{
    [DisallowMultipleComponent]
    public class UIButtonShineEffect : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RectTransform _shineTransform;
        [SerializeField] private CanvasGroup _shineCanvasGroup;
        [SerializeField] private RectTransform _targetArea;

        [Header("Timing")]
        [SerializeField] private bool _playOnEnable = true;
        [SerializeField] private bool _useUnscaledTime = true;
        [SerializeField] private float _initialDelay = 0.75f;
        [SerializeField] private float _travelDuration = 0.85f;
        [SerializeField] private float _repeatDelay = 2.25f;

        [Header("Look")]
        [SerializeField] private float _edgePadding = 120f;
        [SerializeField] private float _startAlpha = 0f;
        [SerializeField] private float _peakAlpha = 0.8f;

        private Tween _startDelayTween;
        private Sequence _shineSequence;

        private void Awake()
        {
            if (_targetArea == null)
            {
                _targetArea = transform as RectTransform;
            }

            if (_shineTransform != null && _shineCanvasGroup == null)
            {
                _shineCanvasGroup = _shineTransform.GetComponent<CanvasGroup>();
            }
        }

        private void OnEnable()
        {
            ResetShineImmediate();

            if (_playOnEnable)
            {
                Play();
            }
        }

        private void OnDisable()
        {
            Stop();
            ResetShineImmediate();
        }

        private void OnDestroy()
        {
            Stop();
        }

        public void Play()
        {
            if (_shineTransform == null || _shineCanvasGroup == null || _targetArea == null)
            {
                return;
            }

            Stop();

            if (_initialDelay > 0f)
            {
                _startDelayTween = DOVirtual.DelayedCall(_initialDelay, StartLoopSequence)
                    .SetUpdate(_useUnscaledTime);
                return;
            }

            StartLoopSequence();
        }

        public void Stop()
        {
            if (_startDelayTween != null && _startDelayTween.IsActive())
            {
                _startDelayTween.Kill();
            }

            _startDelayTween = null;

            if (_shineSequence != null && _shineSequence.IsActive())
            {
                _shineSequence.Kill();
            }

            _shineSequence = null;
        }

        private void StartLoopSequence()
        {
            _shineSequence = DOTween.Sequence()
                .SetUpdate(_useUnscaledTime)
                .SetLoops(-1, LoopType.Restart);

            _shineSequence.AppendCallback(PrepareSweep);
            _shineSequence.Append(_shineTransform
                .DOAnchorPosX(GetEndX(), Mathf.Max(0.05f, _travelDuration))
                .SetEase(Ease.Linear)
                .SetUpdate(_useUnscaledTime));

            _shineSequence.Join(_shineCanvasGroup
                .DOFade(_peakAlpha, Mathf.Max(0.05f, _travelDuration * 0.2f))
                .SetEase(Ease.OutQuad)
                .SetUpdate(_useUnscaledTime));

            _shineSequence.Insert(Mathf.Max(0.05f, _travelDuration * 0.55f), _shineCanvasGroup
                .DOFade(_startAlpha, Mathf.Max(0.05f, _travelDuration * 0.45f))
                .SetEase(Ease.InQuad)
                .SetUpdate(_useUnscaledTime));

            _shineSequence.AppendInterval(Mathf.Max(0f, _repeatDelay));
        }

        private void ResetShineImmediate()
        {
            if (_shineTransform == null)
            {
                return;
            }

            var anchoredPosition = _shineTransform.anchoredPosition;
            _shineTransform.anchoredPosition = new Vector2(GetStartX(), anchoredPosition.y);

            if (_shineCanvasGroup != null)
            {
                _shineCanvasGroup.alpha = _startAlpha;
            }
        }

        private void PrepareSweep()
        {
            if (_shineTransform == null)
            {
                return;
            }

            var anchoredPosition = _shineTransform.anchoredPosition;
            _shineTransform.anchoredPosition = new Vector2(GetStartX(), anchoredPosition.y);

            if (_shineCanvasGroup != null)
            {
                _shineCanvasGroup.alpha = _startAlpha;
            }
        }

        private float GetStartX()
        {
            return -GetHalfWidth() - _edgePadding;
        }

        private float GetEndX()
        {
            return GetHalfWidth() + _edgePadding;
        }

        private float GetHalfWidth()
        {
            return _targetArea != null ? _targetArea.rect.width * 0.5f : 0f;
        }
    }
}
#endif
