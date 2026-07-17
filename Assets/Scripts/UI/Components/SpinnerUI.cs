using UnityEngine;

namespace Serhat.Forge.UI.Components
{
    /// <summary>
    /// Continuously rotates its RectTransform around the Z axis.
    /// Attach to a loading indicator image to create a spinner effect.
    /// </summary>
    public class SpinnerUI : MonoBehaviour
    {
        [Tooltip("Rotation speed in degrees per second")]
        [SerializeField] private float _speed = 360f;

        private RectTransform _rect;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
        }

        private void Update()
        {
            if (_rect != null)
                _rect.Rotate(0f, 0f, -_speed * Time.unscaledDeltaTime);
        }
    }
}
