using System;

namespace Serhat.Localization.Pluralization
{
    /// <summary>
    /// Plural rule for Russian (and similar Slavic languages).
    ///
    /// Rules based on CLDR:
    /// - one: n mod 10 = 1 and n mod 100 != 11
    /// - few: n mod 10 in 2..4 and n mod 100 not in 12..14
    /// - many: n mod 10 = 0 or n mod 10 in 5..9 or n mod 100 in 11..14
    /// - other: (fractional numbers)
    ///
    /// Examples:
    /// - 1, 21, 31, 41... -> one (1 yabloko)
    /// - 2, 3, 4, 22, 23, 24... -> few (2 yabloka)
    /// - 0, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 20, 25... -> many (5 yablok)
    /// - 1.5, 2.5... -> other (1.5 yabloka)
    /// </summary>
    public class RussianPluralRule : IPluralRule
    {
        public string LanguageCode => "ru";

        public PluralCategory GetCategory(decimal number)
        {
            var absoluteValue = Math.Abs(number);

            // Check if it's a whole number
            bool isInteger = absoluteValue == Math.Floor(absoluteValue);

            if (!isInteger)
            {
                return PluralCategory.Other;
            }

            long n = (long)absoluteValue;
            long mod10 = n % 10;
            long mod100 = n % 100;

            // one: n mod 10 = 1 and n mod 100 != 11
            if (mod10 == 1 && mod100 != 11)
            {
                return PluralCategory.One;
            }

            // few: n mod 10 in 2..4 and n mod 100 not in 12..14
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14))
            {
                return PluralCategory.Few;
            }

            // many: everything else for integers
            // (n mod 10 = 0 or n mod 10 in 5..9 or n mod 100 in 11..14)
            return PluralCategory.Many;
        }
    }
}
