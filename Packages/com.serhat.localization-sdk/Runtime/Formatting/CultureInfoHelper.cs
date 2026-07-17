using System;
using System.Collections.Generic;
using System.Globalization;

namespace Serhat.Localization.Formatting
{
    /// <summary>
    /// Helper for getting CultureInfo safely (IL2CPP compatible).
    /// </summary>
    public static class CultureInfoHelper
    {
        private static readonly Dictionary<string, CultureInfo> _cache = new Dictionary<string, CultureInfo>(StringComparer.OrdinalIgnoreCase);

        static CultureInfoHelper()
        {
            // Pre-cache common cultures to avoid reflection at runtime
            CacheCommonCultures();
        }

        private static void CacheCommonCultures()
        {
            var commonLocales = new[]
            {
                "en", "en-US", "en-GB",
                "tr", "tr-TR",
                "ru", "ru-RU",
                "de", "de-DE",
                "fr", "fr-FR",
                "es", "es-ES",
                "it", "it-IT",
                "pt", "pt-BR",
                "ja", "ja-JP",
                "ko", "ko-KR",
                "zh", "zh-CN", "zh-TW",
                "ar", "ar-SA",
                "nl", "nl-NL",
                "pl", "pl-PL"
            };

            foreach (var locale in commonLocales)
            {
                try
                {
                    _cache[locale] = new CultureInfo(locale);
                }
                catch
                {
                    // Culture not available on this platform
                }
            }
        }

        /// <summary>
        /// Gets a CultureInfo for the given locale code.
        /// Returns InvariantCulture if the locale is not available.
        /// </summary>
        public static CultureInfo GetCultureInfo(string localeCode)
        {
            if (string.IsNullOrEmpty(localeCode))
                return CultureInfo.InvariantCulture;

            // Check cache first
            if (_cache.TryGetValue(localeCode, out var cached))
                return cached;

            // Try to create the culture
            try
            {
                var culture = new CultureInfo(localeCode);
                _cache[localeCode] = culture;
                return culture;
            }
            catch
            {
                // Try base language
                var dashIndex = localeCode.IndexOf('-');
                if (dashIndex > 0)
                {
                    var baseLocale = localeCode.Substring(0, dashIndex);
                    if (_cache.TryGetValue(baseLocale, out cached))
                        return cached;

                    try
                    {
                        var culture = new CultureInfo(baseLocale);
                        _cache[baseLocale] = culture;
                        return culture;
                    }
                    catch
                    {
                        // Fall through
                    }
                }

                // Return invariant as last resort
                return CultureInfo.InvariantCulture;
            }
        }

        /// <summary>
        /// Checks if a culture is available on this platform.
        /// </summary>
        public static bool IsCultureAvailable(string localeCode)
        {
            if (string.IsNullOrEmpty(localeCode))
                return false;

            if (_cache.ContainsKey(localeCode))
                return true;

            try
            {
                new CultureInfo(localeCode);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
