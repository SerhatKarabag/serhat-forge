using UnityEngine;

namespace Serhat.Forge.Ads
{
    [CreateAssetMenu(fileName = "AdRuntimeSettings", menuName = "Serhat Forge/Ads/Ad Runtime Settings")]
    public sealed class AdRuntimeSettings : ScriptableObject
    {
        [SerializeField] private bool _enableAds;

        public bool EnableAds => _enableAds;
    }
}
