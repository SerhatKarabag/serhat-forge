using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serhat.Localization.Providers;
using Serhat.Localization.Pluralization;
using Serhat.Localization.Formatting;
using Serhat.Core.Utilities;
using Serhat.Localization.Utilities;
using UnityEngine;

namespace Serhat.Localization
{
    /// <summary>
    /// Core manager for the localization system.
    /// </summary>
    public class LocalizationManager
    {
        private static LocalizationManager _instance;
        private static readonly object _lock = new object();

        private LocalizationSettings _settings;
        private ILocalizationProvider _provider;
        private ILocalizationProvider _fallbackProvider;
        private ILocalizationProvider _defaultProvider;
        private Locale _currentLocale;
        private bool _isInitialized;
        private readonly MissingKeyLogger _missingKeyLogger;
        private readonly LocalizedFormatter _formatter;

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static LocalizationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new LocalizationManager();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Event raised when the locale changes.
        /// </summary>
        public event EventHandler<LocaleChangedEventArgs> LocaleChanged;

        /// <summary>
        /// The current locale.
        /// </summary>
        public Locale CurrentLocale => _currentLocale;

        /// <summary>
        /// Whether the manager is initialized.
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// The settings asset.
        /// </summary>
        public LocalizationSettings Settings => _settings;

        private LocalizationManager()
        {
            _missingKeyLogger = new MissingKeyLogger();
            _formatter = new LocalizedFormatter();
        }

        /// <summary>
        /// Initializes the localization system.
        /// </summary>
        public async Task InitializeAsync(LocalizationSettings settings = null)
        {
            _settings = settings ?? LocalizationSettings.Instance;

            // Determine initial locale
            Locale initialLocale = _settings.DefaultLocale;

            if (_settings.UseSystemLanguage)
            {
                var systemLanguage = GetSystemLanguageCode();
                if (!string.IsNullOrEmpty(systemLanguage))
                {
                    initialLocale = _settings.GetBestMatch(new Locale(systemLanguage));
                }
            }

            // Check for saved preference
            var savedLocale = PlayerPrefs.GetString("Localization_Locale", null);
            if (!string.IsNullOrEmpty(savedLocale) && _settings.IsLocaleSupported(new Locale(savedLocale)))
            {
                initialLocale = new Locale(savedLocale);
            }

            await SetLocaleInternalAsync(initialLocale, false);
            _isInitialized = true;
        }

        /// <summary>
        /// Sets the current locale synchronously.
        /// </summary>
        public void SetLocale(Locale locale)
        {
            SetLocaleAsync(locale).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the current locale asynchronously.
        /// </summary>
        public async Task SetLocaleAsync(Locale locale)
        {
            await SetLocaleInternalAsync(locale, true);
        }

        private async Task SetLocaleInternalAsync(Locale locale, bool savePreference)
        {
            if (!_settings.IsLocaleSupported(locale))
            {
                locale = _settings.GetBestMatch(locale);
            }

            var previousLocale = _currentLocale;
            _currentLocale = locale;

            // Create provider for current locale
            _provider = CreateProvider(_settings.ProviderType);
            await _provider.InitializeAsync(locale.Code);

            // Create fallback provider if locale has region (e.g., en-US -> en)
            _fallbackProvider = null;
            if (locale.HasRegion)
            {
                var languageLocale = locale.GetLanguageLocale();
                if (_settings.IsLocaleSupported(languageLocale) && languageLocale != locale)
                {
                    _fallbackProvider = CreateProvider(_settings.ProviderType);
                    await _fallbackProvider.InitializeAsync(languageLocale.Code);
                }
            }

            // Load default locale as final fallback if different from current
            _defaultProvider = null;
            if (locale != _settings.DefaultLocale)
            {
                _defaultProvider = CreateProvider(_settings.ProviderType);
                await _defaultProvider.InitializeAsync(_settings.DefaultLocale.Code);
            }

            _formatter.SetCulture(locale.Code);

            if (savePreference)
            {
                PlayerPrefs.SetString("Localization_Locale", locale.Code);
                PlayerPrefs.Save();
            }

            // Raise event on main thread
            if (previousLocale != locale)
            {
                MainThreadDispatcher.Enqueue(() =>
                {
                    LocaleChanged?.Invoke(this, new LocaleChangedEventArgs(previousLocale, locale));
                });
            }
        }

        private ILocalizationProvider CreateProvider(ProviderType type)
        {
            return type switch
            {
                ProviderType.StreamingAssets => new StreamingAssetsProvider(_settings.DataPath),
                ProviderType.Resources => new ResourcesProvider(_settings.DataPath),
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
        }

        /// <summary>
        /// Gets a localized string by key.
        /// </summary>
        public string GetString(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            // Try primary provider
            var value = _provider?.GetString(key);
            if (!string.IsNullOrEmpty(value))
                return value;

            // Try language fallback (e.g., en-US -> en)
            if (_fallbackProvider != null)
            {
                value = _fallbackProvider.GetString(key);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            // Try default locale fallback
            if (_defaultProvider != null)
            {
                value = _defaultProvider.GetString(key);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            // Log missing key and return key
            _missingKeyLogger.LogMissingKey(key, _currentLocale.Code);
            return key;
        }

        /// <summary>
        /// Gets a formatted localized string.
        /// </summary>
        public string Format(string key, params object[] args)
        {
            var template = GetString(key);
            return _formatter.Format(template, args);
        }

        /// <summary>
        /// Gets a pluralized localized string.
        /// </summary>
        public string Plural(string key, decimal count, params object[] args)
        {
            var rule = PluralRuleRegistry.GetRule(_currentLocale.Language);
            var category = rule.GetCategory(count);

            // Try to get plural form
            var value = GetPluralString(key, category);
            if (string.IsNullOrEmpty(value))
            {
                // Fall back to "other" form
                value = GetPluralString(key, PluralCategory.Other);
            }

            if (string.IsNullOrEmpty(value))
            {
                // Fall back to base key
                value = GetString(key);
            }

            return _formatter.Format(value, args);
        }

        private string GetPluralString(string key, PluralCategory category)
        {
            // Try primary provider
            var entry = _provider?.GetEntry(key);
            if (entry != null && entry.IsPluralEntry)
            {
                var pluralValue = entry.GetPluralForm(category);
                if (!string.IsNullOrEmpty(pluralValue))
                    return pluralValue;
            }

            // Try language fallback
            if (_fallbackProvider != null)
            {
                entry = _fallbackProvider.GetEntry(key);
                if (entry != null && entry.IsPluralEntry)
                {
                    var pluralValue = entry.GetPluralForm(category);
                    if (!string.IsNullOrEmpty(pluralValue))
                        return pluralValue;
                }
            }

            // Try default locale fallback
            if (_defaultProvider != null)
            {
                entry = _defaultProvider.GetEntry(key);
                if (entry != null && entry.IsPluralEntry)
                {
                    var pluralValue = entry.GetPluralForm(category);
                    if (!string.IsNullOrEmpty(pluralValue))
                        return pluralValue;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets all supported locales.
        /// </summary>
        public IReadOnlyList<Locale> GetSupportedLocales()
        {
            var locales = new List<Locale>();
            foreach (var code in _settings.SupportedLocales)
            {
                locales.Add(new Locale(code));
            }
            return locales;
        }

        private string GetSystemLanguageCode()
        {
            return Application.systemLanguage switch
            {
                SystemLanguage.English => "en",
                SystemLanguage.Turkish => "tr",
                SystemLanguage.Russian => "ru",
                SystemLanguage.German => "de",
                SystemLanguage.French => "fr",
                SystemLanguage.Spanish => "es",
                SystemLanguage.Italian => "it",
                SystemLanguage.Portuguese => "pt",
                SystemLanguage.Japanese => "ja",
                SystemLanguage.Korean => "ko",
                SystemLanguage.Chinese => "zh",
                SystemLanguage.ChineseSimplified => "zh-CN",
                SystemLanguage.ChineseTraditional => "zh-TW",
                SystemLanguage.Arabic => "ar",
                SystemLanguage.Dutch => "nl",
                SystemLanguage.Polish => "pl",
                _ => null
            };
        }

#if UNITY_EDITOR
        /// <summary>
        /// Resets the instance (for testing).
        /// </summary>
        public static void Reset()
        {
            _instance = null;
        }
#endif
    }
}
