using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Serhat.Localization
{
    /// <summary>
    /// Static facade for easy access to localization functionality.
    /// </summary>
    public static class Loc
    {
        /// <summary>
        /// Event raised when the locale changes.
        /// </summary>
        public static event EventHandler<LocaleChangedEventArgs> OnLocaleChanged
        {
            add => LocalizationManager.Instance.LocaleChanged += value;
            remove => LocalizationManager.Instance.LocaleChanged -= value;
        }

        /// <summary>
        /// The current locale.
        /// </summary>
        public static Locale CurrentLocale => LocalizationManager.Instance.CurrentLocale;

        /// <summary>
        /// Whether the localization system is initialized.
        /// </summary>
        public static bool IsInitialized => LocalizationManager.Instance.IsInitialized;

        /// <summary>
        /// Initializes the localization system.
        /// </summary>
        public static Task InitializeAsync(LocalizationSettings settings = null)
        {
            return LocalizationManager.Instance.InitializeAsync(settings);
        }

        /// <summary>
        /// Gets a localized string by key.
        /// </summary>
        /// <param name="key">The localization key.</param>
        /// <returns>The localized string, or the key if not found.</returns>
        public static string Get(string key)
        {
            return LocalizationManager.Instance.GetString(key);
        }

        /// <summary>
        /// Gets a formatted localized string.
        /// </summary>
        /// <param name="key">The localization key.</param>
        /// <param name="args">Format arguments.</param>
        /// <returns>The formatted localized string.</returns>
        public static string Format(string key, params object[] args)
        {
            return LocalizationManager.Instance.Format(key, args);
        }

        /// <summary>
        /// Gets a pluralized localized string.
        /// </summary>
        /// <param name="key">The base localization key.</param>
        /// <param name="count">The count for pluralization.</param>
        /// <param name="args">Format arguments.</param>
        /// <returns>The pluralized and formatted string.</returns>
        public static string Plural(string key, int count, params object[] args)
        {
            return LocalizationManager.Instance.Plural(key, count, args);
        }

        /// <summary>
        /// Gets a pluralized localized string.
        /// </summary>
        /// <param name="key">The base localization key.</param>
        /// <param name="count">The count for pluralization.</param>
        /// <param name="args">Format arguments.</param>
        /// <returns>The pluralized and formatted string.</returns>
        public static string Plural(string key, decimal count, params object[] args)
        {
            return LocalizationManager.Instance.Plural(key, count, args);
        }

        /// <summary>
        /// Sets the current locale synchronously.
        /// </summary>
        /// <param name="locale">The locale code.</param>
        public static void SetLocale(string locale)
        {
            LocalizationManager.Instance.SetLocale(new Locale(locale));
        }

        /// <summary>
        /// Sets the current locale asynchronously.
        /// </summary>
        /// <param name="locale">The locale code.</param>
        public static Task SetLocaleAsync(string locale)
        {
            return LocalizationManager.Instance.SetLocaleAsync(new Locale(locale));
        }

        /// <summary>
        /// Gets all supported locales.
        /// </summary>
        public static IReadOnlyList<Locale> GetSupportedLocales()
        {
            return LocalizationManager.Instance.GetSupportedLocales();
        }
    }
}
