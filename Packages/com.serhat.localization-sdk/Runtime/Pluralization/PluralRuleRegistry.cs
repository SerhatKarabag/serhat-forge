using System;
using System.Collections.Generic;

namespace Serhat.Localization.Pluralization
{
    /// <summary>
    /// Registry for plural rules.
    /// </summary>
    public static class PluralRuleRegistry
    {
        private static readonly Dictionary<string, IPluralRule> _rules = new Dictionary<string, IPluralRule>(StringComparer.OrdinalIgnoreCase);
        private static readonly IPluralRule _defaultRule = new EnglishPluralRule();

        static PluralRuleRegistry()
        {
            // Register built-in rules
            Register(new EnglishPluralRule());
            Register(new TurkishPluralRule());
            Register(new RussianPluralRule());
        }

        /// <summary>
        /// Registers a plural rule.
        /// </summary>
        public static void Register(IPluralRule rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            _rules[rule.LanguageCode] = rule;
        }

        /// <summary>
        /// Gets the plural rule for a language.
        /// </summary>
        /// <param name="languageCode">The language code.</param>
        /// <returns>The plural rule, or the default (English) rule if not found.</returns>
        public static IPluralRule GetRule(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
                return _defaultRule;

            if (_rules.TryGetValue(languageCode, out var rule))
                return rule;

            return _defaultRule;
        }

        /// <summary>
        /// Checks if a rule exists for a language.
        /// </summary>
        public static bool HasRule(string languageCode)
        {
            return _rules.ContainsKey(languageCode);
        }

        /// <summary>
        /// Gets all registered language codes.
        /// </summary>
        public static IEnumerable<string> GetRegisteredLanguages()
        {
            return _rules.Keys;
        }
    }
}
