#if DOTWEEN
using Serhat.Forge.Audio;
using Zenject;
using UnityEngine;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Button helper that plays UI click feedback before forwarding to a PopupToggle action.
    /// Useful for prefab buttons that cannot reference scene-only objects like SoundManager.
    /// </summary>
    public sealed class PopupToggleButton : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PopupToggle _popupToggle;

        [Header("Feedback")]
        [SerializeField] private bool _playButtonClick = true;

        [Inject] private ISfxService _sfxService;

        private void Awake()
        {
            if (_popupToggle == null)
                _popupToggle = GetComponent<PopupToggle>();
        }

        public void Open()
        {
            if (!TryPrepareAction())
                return;

            _popupToggle.Open();
        }

        public void Close()
        {
            if (!TryPrepareAction())
                return;

            _popupToggle.Close();
        }

        public void Toggle()
        {
            if (!TryPrepareAction())
                return;

            _popupToggle.Toggle();
        }

        private bool TryPrepareAction()
        {
            if (_popupToggle == null)
            {
                Debug.LogWarning($"[PopupToggleButton] PopupToggle is missing on {gameObject.name}.", this);
                return false;
            }

            if (_playButtonClick)
            {
                if (_sfxService == null)
                    Debug.LogWarning($"[PopupToggleButton] _sfxService is NULL on {gameObject.name}. Injection may have failed.", this);
                else
                    Debug.Log($"[PopupToggleButton] Playing button click on {gameObject.name}", this);

                _sfxService?.PlayButtonClick();
            }

            return true;
        }
    }
}
#endif
