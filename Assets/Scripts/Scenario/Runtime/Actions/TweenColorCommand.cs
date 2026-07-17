using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that tweens a color on a Graphic (UI) or SpriteRenderer.
    /// </summary>
    [Serializable]
    public class TweenColorCommand : BaseCommand
    {
        public enum TargetType
        {
            Graphic,
            SpriteRenderer
        }

        [SerializeField]
        private TargetType _targetType = TargetType.Graphic;

        [SerializeField]
        private ProxyObjectSaver<Graphic> _graphic = new ProxyObjectSaver<Graphic>();

        [SerializeField]
        private ProxyObjectSaver<SpriteRenderer> _spriteRenderer = new ProxyObjectSaver<SpriteRenderer>();

        [SerializeField]
        private Color _startColor = Color.white;

        [SerializeField]
        private Color _endColor = Color.white;

        [SerializeField]
        private float _duration = 1f;

        [SerializeField]
        private AnimationCurve _curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField]
        private bool _setStartColorOnExecute = true;

        private bool _isCancelled;

        public TargetType Type
        {
            get => _targetType;
            set => _targetType = value;
        }

        public Graphic Graphic
        {
            get => _graphic.Value;
            set => _graphic.Value = value;
        }

        public SpriteRenderer SpriteRenderer
        {
            get => _spriteRenderer.Value;
            set => _spriteRenderer.Value = value;
        }

        public Color StartColor
        {
            get => _startColor;
            set => _startColor = value;
        }

        public Color EndColor
        {
            get => _endColor;
            set => _endColor = value;
        }

        public float Duration
        {
            get => _duration;
            set => _duration = value;
        }

        public TweenColorCommand()
        {
        }

        public override async Task Execute()
        {
            _isCancelled = false;

            if (_targetType == TargetType.Graphic && _graphic.Value == null)
            {
                Debug.LogWarning("[TweenColorCommand] Graphic is null.");
                return;
            }

            if (_targetType == TargetType.SpriteRenderer && _spriteRenderer.Value == null)
            {
                Debug.LogWarning("[TweenColorCommand] SpriteRenderer is null.");
                return;
            }

            if (_setStartColorOnExecute)
            {
                SetColor(_startColor);
            }

            float elapsed = 0f;

            while (elapsed < _duration && !_isCancelled)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _duration);
                float curveValue = _curve.Evaluate(t);

                Color newColor = Color.LerpUnclamped(_startColor, _endColor, curveValue);
                SetColor(newColor);

                await Task.Yield();
            }

            if (!_isCancelled)
            {
                SetColor(_endColor);
            }
        }

        private void SetColor(Color color)
        {
            if (_targetType == TargetType.Graphic && _graphic.Value != null)
            {
                _graphic.Value.color = color;
            }
            else if (_targetType == TargetType.SpriteRenderer && _spriteRenderer.Value != null)
            {
                _spriteRenderer.Value.color = color;
            }
        }

        public override void Stop()
        {
            _isCancelled = true;
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _graphic.Save(saver);
            _spriteRenderer.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _graphic.Restore(saver);
            _spriteRenderer.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _graphic.ReleaseResources(saver);
            _spriteRenderer.ReleaseResources(saver);
        }
    }
}
