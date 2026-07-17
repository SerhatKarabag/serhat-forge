using System;

namespace Serhat.Localization.Pluralization
{
    /// <summary>
    /// Plural rule for Turkish.
    /// Turkish has no grammatical plural for counted nouns (e.g., "5 elma" not "5 elmalar").
    /// When a number precedes a noun, the noun stays singular.
    /// However, for localization purposes, we support "one" for n=1 to allow
    /// different phrasing if needed (e.g., "1 oge" vs "{n} oge").
    /// </summary>
    public class TurkishPluralRule : IPluralRule
    {
        public string LanguageCode => "tr";

        public PluralCategory GetCategory(decimal number)
        {
            var absoluteValue = Math.Abs(number);

            // Turkish: Use "one" for exactly 1, "other" for everything else
            // This allows translators flexibility while respecting Turkish grammar
            if (absoluteValue == 1m && number == Math.Floor(number))
            {
                return PluralCategory.One;
            }

            return PluralCategory.Other;
        }
    }
}
