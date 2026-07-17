using System;
using System.Globalization;

namespace Serhat.Localization.Formatting
{
    /// <summary>
    /// Handles culture-aware string formatting.
    /// </summary>
    public class LocalizedFormatter
    {
        private CultureInfo _culture;

        public LocalizedFormatter()
        {
            _culture = CultureInfo.InvariantCulture;
        }

        /// <summary>
        /// Sets the culture for formatting.
        /// </summary>
        public void SetCulture(string localeCode)
        {
            _culture = CultureInfoHelper.GetCultureInfo(localeCode);
        }

        /// <summary>
        /// Formats a string with the current culture.
        /// </summary>
        public string Format(string template, params object[] args)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            if (args == null || args.Length == 0)
                return template;

            try
            {
                return string.Format(_culture, template, args);
            }
            catch (FormatException)
            {
                // Return template as-is if formatting fails
                return template;
            }
        }

        /// <summary>
        /// Formats a number with the current culture.
        /// </summary>
        public string FormatNumber(decimal number, string format = null)
        {
            return number.ToString(format, _culture);
        }

        /// <summary>
        /// Formats a date with the current culture.
        /// </summary>
        public string FormatDate(DateTime date, string format = null)
        {
            return date.ToString(format ?? "d", _culture);
        }

        /// <summary>
        /// Formats a currency value.
        /// </summary>
        public string FormatCurrency(decimal amount, string currencySymbol = null)
        {
            if (currencySymbol != null)
            {
                return string.Format(_culture, "{0:N2} {1}", amount, currencySymbol);
            }
            return amount.ToString("C", _culture);
        }
    }
}
