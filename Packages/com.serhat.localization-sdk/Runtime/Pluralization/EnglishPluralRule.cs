using System;

namespace Serhat.Localization.Pluralization
{
    /// <summary>
    /// Plural rule for English (and similar languages).
    /// Rule: one for n=1, other for everything else.
    /// </summary>
    public class EnglishPluralRule : IPluralRule
    {
        public string LanguageCode => "en";

        public PluralCategory GetCategory(decimal number)
        {
            var absoluteValue = Math.Abs(number);

            // Check if it's exactly 1 (integer)
            if (absoluteValue == 1m && number == Math.Floor(number))
            {
                return PluralCategory.One;
            }

            return PluralCategory.Other;
        }
    }
}
