using Serhat.Forge.Haptics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// A button component that provides a press-down visual effect.
    /// Works with a two-part button design: a background (shadow) and a foreground (top) part.
    /// When pressed, the top part moves down to create a "pressed" feeling.
    /// </summary>
    public class PressableButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        public enum HapticTrigger
        {
            OnPress,
            OnClick,
        }

        #region Inspector Fields

        [Header("References")]
        [Tooltip("The top part of the button that moves down when pressed")]
        [SerializeField] private RectTransform _topPart;

        [Tooltip("Image component on top part for color tinting (optional)")]
        [SerializeField] private Image _topPartImage;

        [Header("Scale Settings")]
        [Tooltip("Enable scale effect when pressed")]
        [SerializeField] private bool _useScaleEffect = true;

        [Tooltip("Scale multiplier when pressed (0.95 = 95% of original size)")]
        [SerializeField] private float _pressedScale = 0.95f;

        [Header("Color Settings")]
        [Tooltip("Enable color darkening when pressed")]
        [SerializeField] private bool _useColorEffect = true;

        [Tooltip("Color multiplier when pressed (darker)")]
        [SerializeField] private Color _pressedColorMultiplier = new Color(0.85f, 0.85f, 0.85f, 1f);

        [Header("Sprite Settings")]
        [Tooltip("Swap the top image sprite while the button is pressed")]
        [SerializeField] private bool _useSpriteSwap;

        [Tooltip("Optional normal sprite. If empty, current top image sprite is used.")]
        [SerializeField] private Sprite _normalSprite;

        [Tooltip("Sprite to show while pressed. If empty, sprite will not change.")]
        [SerializeField] private Sprite _pressedSprite;

        [Header("Audio Settings")]
        [Tooltip("Sound to play when button is pressed down")]
        [SerializeField] private AudioClip _pressSound;

        [Tooltip("Sound to play when button is clicked")]
        [SerializeField] private AudioClip _clickSound;

        [Tooltip("AudioSource to use (if null, will use AudioSource.PlayClipAtPoint)")]
        [SerializeField] private AudioSource _audioSource;

        [Header("Haptic Settings")]
        [Tooltip("Enable haptic feedback when the button is pressed/clicked")]
        [SerializeField] private bool _useHaptic = false;

        [Tooltip("Haptic preset to play (matches the project-wide HapticHelper presets)")]
        [SerializeField] private HapticHelper.Preset _hapticType = HapticHelper.Preset.Selection;

        [Tooltip("When to fire the haptic: OnPress fires instantly on pointer-down, OnClick fires after release")]
        [SerializeField] private HapticTrigger _hapticTrigger = HapticTrigger.OnPress;

        [Header("Animation Settings")]
        [Tooltip("Use smooth animation for press/release")]
        [SerializeField] private bool _useAnimation = true;

        [Tooltip("Animation speed (higher = faster)")]
        [SerializeField] private float _animationSpeed = 15f;

        [Header("Events")]
        [SerializeField] private UnityEvent _onClick;

        #endregion

        #region Private Fields

        private Vector3 _originalScale;
        private Vector3 _pressedScaleVector;
        private Color _originalColor;
        private Color _pressedColor;
        private Sprite _originalSprite;
        private bool _isPressed;
        private bool _isInitialized;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            Initialize();
        }

        private void Update()
        {
            if (!_useAnimation || _topPart == null)
                return;

            float lerpFactor = Time.unscaledDeltaTime * _animationSpeed;

            // Smoothly animate scale
            if (_useScaleEffect)
            {
                Vector3 targetScale = _isPressed ? _pressedScaleVector : _originalScale;
                _topPart.localScale = Vector3.Lerp(_topPart.localScale, targetScale, lerpFactor);
            }

            // Smoothly animate color
            if (_useColorEffect && _topPartImage != null)
            {
                Color targetColor = _isPressed ? _pressedColor : _originalColor;
                _topPartImage.color = Color.Lerp(_topPartImage.color, targetColor, lerpFactor);
            }
        }

        #endregion

        #region Initialization

        private void Initialize()
        {
            if (_isInitialized || _topPart == null)
                return;

            // Scale
            _originalScale = _topPart.localScale;
            _pressedScaleVector = _originalScale * _pressedScale;

            // Color
            if (_topPartImage != null)
            {
                _originalColor = _topPartImage.color;
                _originalSprite = _topPartImage.sprite;
                _pressedColor = new Color(
                    _originalColor.r * _pressedColorMultiplier.r,
                    _originalColor.g * _pressedColorMultiplier.g,
                    _originalColor.b * _pressedColorMultiplier.b,
                    _originalColor.a * _pressedColorMultiplier.a
                );

                ApplySpriteState(false);
            }

            _isInitialized = true;
        }

        #endregion

        #region Pointer Events

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_topPart == null)
                return;

            _isPressed = true;

            // Play press sound
            PlaySound(_pressSound);
            TryPlayHaptic(HapticTrigger.OnPress);
            ApplySpriteState(true);

            if (!_useAnimation)
            {
                ApplyPressedState();
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_topPart == null)
                return;

            _isPressed = false;
            ApplySpriteState(false);

            if (!_useAnimation)
            {
                ApplyNormalState();
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_topPart == null)
                return;

            _isPressed = false;
            ApplySpriteState(false);

            if (!_useAnimation)
            {
                ApplyNormalState();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Play click sound
            PlaySound(_clickSound);
            TryPlayHaptic(HapticTrigger.OnClick);

            _onClick?.Invoke();
        }

        #endregion

        #region State Application

        private void ApplyPressedState()
        {
            if (_useScaleEffect)
            {
                _topPart.localScale = _pressedScaleVector;
            }

            if (_useColorEffect && _topPartImage != null)
            {
                _topPartImage.color = _pressedColor;
            }

            ApplySpriteState(true);
        }

        private void ApplyNormalState()
        {
            if (_useScaleEffect)
            {
                _topPart.localScale = _originalScale;
            }

            if (_useColorEffect && _topPartImage != null)
            {
                _topPartImage.color = _originalColor;
            }

            ApplySpriteState(false);
        }

        private void ApplySpriteState(bool pressed)
        {
            if (!_useSpriteSwap || _topPartImage == null)
            {
                return;
            }

            var normalSprite = _normalSprite != null ? _normalSprite : _originalSprite;
            var targetSprite = pressed && _pressedSprite != null ? _pressedSprite : normalSprite;

            if (_topPartImage.sprite != targetSprite)
            {
                _topPartImage.sprite = targetSprite;
            }
        }

        #endregion

        #region Audio

        private void PlaySound(AudioClip clip)
        {
            if (clip == null)
                return;

            if (_audioSource != null)
            {
                _audioSource.PlayOneShot(clip);
            }
            else
            {
                AudioSource.PlayClipAtPoint(clip, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            }
        }

        #endregion

        #region Haptics

        private void TryPlayHaptic(HapticTrigger trigger)
        {
            if (!_useHaptic || _hapticTrigger != trigger)
                return;

            if (_hapticType == HapticHelper.Preset.None)
                return;

            HapticHelper.PlayPreset(_hapticType);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Add a listener to the click event.
        /// </summary>
        public void AddClickListener(UnityAction action)
        {
            _onClick.AddListener(action);
        }

        /// <summary>
        /// Remove a listener from the click event.
        /// </summary>
        public void RemoveClickListener(UnityAction action)
        {
            _onClick.RemoveListener(action);
        }


        /// <summary>
        /// Removes all click listeners, including persistent Inspector listeners.
        /// </summary>
        public void RemoveAllClickListeners()
        {
            _onClick.RemoveAllListeners();
        }
        /// <summary>
        /// Simulate a button press programmatically.
        /// </summary>
        public void SimulatePress()
        {
            _isPressed = true;
            ApplySpriteState(true);

            if (!_useAnimation && _topPart != null)
            {
                ApplyPressedState();
            }
        }

        /// <summary>
        /// Simulate a button release programmatically.
        /// </summary>
        public void SimulateRelease()
        {
            _isPressed = false;
            ApplySpriteState(false);

            if (!_useAnimation && _topPart != null)
            {
                ApplyNormalState();
            }
        }

        /// <summary>
        /// Set interactable state.
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            enabled = interactable;

            if (!interactable && _isPressed)
            {
                _isPressed = false;
                ApplyNormalState();
            }
        }

        /// <summary>
        /// Enables or disables haptic feedback at runtime, optionally overriding the preset and trigger.
        /// </summary>
        public void SetHaptic(bool useHaptic, HapticHelper.Preset preset = HapticHelper.Preset.Selection, HapticTrigger trigger = HapticTrigger.OnPress)
        {
            _useHaptic = useHaptic;
            _hapticType = preset;
            _hapticTrigger = trigger;
        }

        /// <summary>
        /// Updates the sprite swap pair used by this button.
        /// When sprite swap is enabled, the current visual state is refreshed immediately.
        /// </summary>
        public void SetSpriteSwapSprites(Sprite normalSprite, Sprite pressedSprite, bool enableSpriteSwap = true)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            _useSpriteSwap = enableSpriteSwap;
            _normalSprite = normalSprite;
            _pressedSprite = pressedSprite;

            if (_topPartImage != null)
            {
                ApplySpriteState(_isPressed);
            }
        }

        #endregion
    }
}
