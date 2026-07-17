using System;
using System.Collections.Generic;
using UnityEngine;

namespace Serhat.Localization
{
    /// <summary>
    /// Configuration asset for the localization system.
    /// </summary>
    [CreateAssetMenu(fileName = "LocalizationSettings", menuName = "Serhat/Localization/Settings")]
    public class LocalizationSettings : ScriptableObject
    {
        private const string ResourcePath = "LocalizationSettings";

        [SerializeField]
        [Tooltip("The default locale to use when no preference is set.")]
        private string _defaultLocale = "en";

        [SerializeField]
        [Tooltip("List of supported locale codes.")]
        private List<string> _supportedLocales = new List<string> { "en", "tr" };

        [SerializeField]
        [Tooltip("The provider type to use for loading localization data.")]
        private ProviderType _providerType = ProviderType.StreamingAssets;

        [SerializeField]
        [Tooltip("Path relative to StreamingAssets or Resources folder.")]
        private string _dataPath = "Localization/Locales";

        [SerializeField]
        [Tooltip("Whether to auto-initialize on application start.")]
        private bool _autoInitialize = true;

        [SerializeField]
        [Tooltip("Whether to use system language as initial locale if supported.")]
        private bool _useSystemLanguage = true;

        /// <summary>
        /// The default locale code.
        /// </summary>
        public Locale DefaultLocale => new Locale(_defaultLocale);

        /// <summary>
        /// List of supported locales.
        /// </summary>
        public IReadOnlyList<string> SupportedLocales => _supportedLocales;

        /// <summary>
        /// The provider type for loading data.
        /// </summary>
        public ProviderType ProviderType => _providerType;

        /// <summary>
        /// Path to localization data.
        /// </summary>
        public string DataPath => _dataPath;

        /// <summary>
        /// Whether to auto-initialize.
        /// </summary>
        public bool AutoInitialize => _autoInitialize;

        /// <summary>
        /// Whether to use system language.
        /// </summary>
        public bool UseSystemLanguage => _useSystemLanguage;

        /// <summary>
        /// Checks if a locale is supported.
        /// </summary>
        public bool IsLocaleSupported(Locale locale)
        {
            if (string.IsNullOrEmpty(locale.Code)) return false;

            foreach (var supported in _supportedLocales)
            {
                if (string.Equals(supported, locale.Code, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a language is supported (ignoring region).
        /// </summary>
        public bool IsLanguageSupported(string language)
        {
            if (string.IsNullOrEmpty(language)) return false;

            foreach (var supported in _supportedLocales)
            {
                var supportedLocale = new Locale(supported);
                if (string.Equals(supportedLocale.Language, language, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the best matching locale for a given locale code.
        /// </summary>
        public Locale GetBestMatch(Locale locale)
        {
            // Exact match
            if (IsLocaleSupported(locale))
                return locale;

            // Language match (e.g., en-US -> en)
            if (locale.HasRegion && IsLanguageSupported(locale.Language))
            {
                foreach (var supported in _supportedLocales)
                {
                    var supportedLocale = new Locale(supported);
                    if (string.Equals(supportedLocale.Language, locale.Language, StringComparison.OrdinalIgnoreCase))
                        return supportedLocale;
                }
            }

            // Fall back to default
            return DefaultLocale;
        }

        private static LocalizationSettings _instance;

        /// <summary>
        /// Gets the settings instance from Resources.
        /// </summary>
        public static LocalizationSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<LocalizationSettings>(ResourcePath);
                    if (_instance == null)
                    {
                        Debug.LogWarning($"[Localization] No LocalizationSettings found at Resources/{ResourcePath}. Using defaults.");
                        _instance = CreateInstance<LocalizationSettings>();
                    }
                }
                return _instance;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Sets the instance (for editor use only).
        /// </summary>
        public static void SetInstance(LocalizationSettings settings)
        {
            _instance = settings;
        }
#endif
    }

    /// <summary>
    /// Types of localization data providers.
    /// </summary>
    public enum ProviderType
    {
        StreamingAssets,
        Resources
    }
}
