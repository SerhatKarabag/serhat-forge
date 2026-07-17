#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Serhat.Localization;

namespace Serhat.Forge.Localization
{
    /// <summary>
    /// Unity-facing localization facade for Serhat Forge.
    /// Wraps the package API with lifecycle, language switching, and UI-friendly metadata.
    /// </summary>
    public class LocalizationManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool _initializeOnAwake = true;

        private bool _isSubscribed;
        private bool _isDestroyed;
        private Task? _initializationTask;

        /// <summary>
        /// Event raised when the language changes.
        /// </summary>
        public event Action<string>? OnLanguageChanged;

        /// <summary>
        /// Whether the localization system is initialized.
        /// </summary>
        public bool IsInitialized => Loc.IsInitialized;

        /// <summary>
        /// Current language code (e.g., "en", "tr").
        /// </summary>
        public string CurrentLanguage => Loc.CurrentLocale.Code ?? string.Empty;

        /// <summary>
        /// Display name of the current language.
        /// </summary>
        public string CurrentLanguageDisplayName => GetLanguageDisplayName(CurrentLanguage);

        private void Awake()
        {
            if (_initializeOnAwake)
            {
                _ = InitializeAsync();
            }
        }

        /// <summary>
        /// Initializes the localization system.
        /// </summary>
        public Task InitializeAsync()
        {
            if (Loc.IsInitialized)
            {
                EnsureSubscribed();
                return Task.CompletedTask;
            }

            return _initializationTask ??= InitializeCoreAsync();
        }

        private async Task InitializeCoreAsync()
        {
            try
            {
                await Loc.InitializeAsync();
                EnsureSubscribed();

                Debug.Log($"[Localization] Initialized with language: {CurrentLanguage}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Localization] Failed to initialize: {ex.Message}");
            }
            finally
            {
                _initializationTask = null;
            }
        }

        private void EnsureSubscribed()
        {
            if (_isDestroyed || _isSubscribed)
                return;

            Loc.OnLocaleChanged += HandleLocaleChanged;
            _isSubscribed = true;
        }

        private void HandleLocaleChanged(object? sender, LocaleChangedEventArgs e)
        {
            Debug.Log($"[Localization] Language changed: {e.PreviousLocale} -> {e.NewLocale}");
            OnLanguageChanged?.Invoke(e.NewLocale.Code);
        }

        #region Language Switching

        /// <summary>
        /// Changes the current language.
        /// </summary>
        /// <param name="languageCode">Language code (e.g., "en", "tr").</param>
        public async Task SetLanguageAsync(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                Debug.LogWarning("[Localization] Invalid language code");
                return;
            }

            await Loc.SetLocaleAsync(languageCode);
        }

        /// <summary>
        /// Changes the current language synchronously.
        /// </summary>
        public void SetLanguage(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                Debug.LogWarning("[Localization] Invalid language code");
                return;
            }

            Loc.SetLocale(languageCode);
        }

        /// <summary>
        /// Gets all supported languages.
        /// </summary>
        public List<LanguageInfo> GetSupportedLanguages()
        {
            var languages = new List<LanguageInfo>();
            var locales = Loc.GetSupportedLocales();

            foreach (var locale in locales)
            {
                languages.Add(new LanguageInfo
                {
                    Code = locale.Code,
                    DisplayName = GetLanguageDisplayName(locale.Code),
                    NativeName = GetLanguageNativeName(locale.Code),
                    IsCurrent = locale.Code == CurrentLanguage
                });
            }

            return languages;
        }

        /// <summary>
        /// Cycles to the next available language.
        /// </summary>
        public async Task CycleLanguageAsync()
        {
            var locales = Loc.GetSupportedLocales();
            if (locales.Count <= 1) return;

            int currentIndex = -1;
            for (int i = 0; i < locales.Count; i++)
            {
                if (locales[i].Code == CurrentLanguage)
                {
                    currentIndex = i;
                    break;
                }
            }

            int nextIndex = (currentIndex + 1) % locales.Count;
            await SetLanguageAsync(locales[nextIndex].Code);
        }

        #endregion

        #region Quick Access Methods

        /// <summary>
        /// Gets a localized string by key.
        /// </summary>
        public static string Get(string key)
        {
            return Loc.Get(key);
        }

        /// <summary>
        /// Gets a formatted localized string.
        /// </summary>
        public static string Format(string key, params object[] args)
        {
            return Loc.Format(key, args);
        }

        /// <summary>
        /// Gets a pluralized localized string.
        /// </summary>
        public static string Plural(string key, int count, params object[] args)
        {
            return Loc.Plural(key, count, args);
        }

        #endregion

        #region Helper Methods

        private string GetLanguageDisplayName(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            return code.ToLowerInvariant() switch
            {
                "en" => "English",
                "tr" => "Turkish",
                "de" => "German",
                "fr" => "French",
                "es" => "Spanish",
                "it" => "Italian",
                "pt" => "Portuguese",
                "ru" => "Russian",
                "ja" => "Japanese",
                "ko" => "Korean",
                "zh" => "Chinese",
                "ar" => "Arabic",
                _ => code.ToUpperInvariant()
            };
        }

        private string GetLanguageNativeName(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            return code.ToLowerInvariant() switch
            {
                "en" => "English",
                "tr" => "Türkçe",
                "de" => "Deutsch",
                "fr" => "Français",
                "es" => "Español",
                "it" => "Italiano",
                "pt" => "Português",
                "ru" => "Русский",
                "ja" => "日本語",
                "ko" => "한국어",
                "zh" => "中文",
                "ar" => "العربية",
                _ => code.ToUpperInvariant()
            };
        }

        #endregion

        #region Lifecycle

        private void OnDestroy()
        {
            _isDestroyed = true;
            if (_isSubscribed)
            {
                Loc.OnLocaleChanged -= HandleLocaleChanged;
                _isSubscribed = false;
            }
        }

        #endregion
    }

    /// <summary>
    /// Information about a supported language.
    /// </summary>
    [Serializable]
    public struct LanguageInfo
    {
        public string Code;
        public string DisplayName;
        public string NativeName;
        public bool IsCurrent;
    }
}
