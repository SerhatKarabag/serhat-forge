using TMPro;
using UnityEngine;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Animates trailing dots on a TMP text (e.g. "Loading" → "Loading." → "Loading.." → "Loading...").
    /// Works with Time.timeScale = 0 (unscaled time).
    /// </summary>
    public class LoadingDotsAnimator : MonoBehaviour
    {
        [SerializeField] private TMP_Text _text;
        [SerializeField] private string _baseText = "Loading";
        [SerializeField] private int _maxDots = 3;
        [SerializeField] private float _interval = 0.4f;

        private float _timer;
        private int _dotCount;

        private void OnEnable()
        {
            _dotCount = 0;
            _timer = 0f;
            UpdateText();
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < _interval) return;

            _timer -= _interval;
            _dotCount = (_dotCount + 1) % (_maxDots + 1);
            UpdateText();
        }

        private void UpdateText()
        {
            if (_text == null) return;
            _text.text = _baseText + new string('.', _dotCount);
        }
    }
}
