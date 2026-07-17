namespace Serhat.Forge.Core
{
    /// <summary>Centralized generic Addressables and preference keys.</summary>
    public static class ResourcePaths
    {
        public static class Addressables
        {
            public static class Prefabs
            {
                // Add stable project prefab keys here.
            }

            public static class Config
            {
                public const string FeatureGateConfig = "FeatureGateConfig";
                public const string ProgressUnlockCatalog = "LevelUnlockCatalog";
            }
        }

        public static class PlayerPrefs
        {
            public const string SoundEnabled = "SoundEnabled";
            public const string MusicEnabled = "MusicEnabled";
            public const string HapticsEnabled = "HapticsEnabled";
            public const string Locale = "Locale";
        }
    }
}
