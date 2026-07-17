using UnityEngine;

namespace Serhat.Forge.Core
{
    /// <summary>
    /// Optional application-wide runtime policy loaded from Resources before the first scene.
    /// Disabled policies leave the corresponding Unity project settings untouched.
    /// </summary>
    [CreateAssetMenu(
        fileName = ResourceName,
        menuName = "Serhat Forge/Config/App Runtime Settings")]
    public sealed class AppRuntimeSettings : ScriptableObject
    {
        public const string ResourceName = "AppRuntimeSettings";

        [Header("Frame Policy")]
        [Tooltip("When disabled, vSync and targetFrameRate remain controlled by Unity/project settings.")]
        [SerializeField] private bool _applyFramePolicy = false;

        [Tooltip("Applied to QualitySettings.vSyncCount when frame policy is enabled.")]
        [SerializeField, Range(0, 4)] private int _vSyncCount;

        [Tooltip("Applied to Application.targetFrameRate when frame policy is enabled. Use -1 for platform default.")]
        [SerializeField, Min(-1)] private int _targetFrameRate = 60;

        public bool ApplyFramePolicy => _applyFramePolicy;
        public int VSyncCount => Mathf.Clamp(_vSyncCount, 0, 4);
        public int TargetFrameRate => _targetFrameRate == 0 ? -1 : Mathf.Max(-1, _targetFrameRate);

#if UNITY_EDITOR
        private void OnValidate()
        {
            _vSyncCount = Mathf.Clamp(_vSyncCount, 0, 4);
            if (_targetFrameRate == 0 || _targetFrameRate < -1)
            {
                _targetFrameRate = -1;
            }
        }
#endif
    }
}
