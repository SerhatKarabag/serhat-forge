using System;

namespace Serhat.Localization
{
    /// <summary>
    /// Represents a locale identified by an IETF language tag.
    /// </summary>
    [Serializable]
    public readonly struct Locale : IEquatable<Locale>
    {
        /// <summary>
        /// The full locale code (e.g., "en-US", "tr").
        /// </summary>
        public readonly string Code;

        /// <summary>
        /// The language portion of the locale (e.g., "en" from "en-US").
        /// </summary>
        public string Language
        {
            get
            {
                if (string.IsNullOrEmpty(Code)) return string.Empty;
                var dashIndex = Code.IndexOf('-');
                return dashIndex > 0 ? Code.Substring(0, dashIndex) : Code;
            }
        }

        /// <summary>
        /// The region portion of the locale (e.g., "US" from "en-US"), or empty if not specified.
        /// </summary>
        public string Region
        {
            get
            {
                if (string.IsNullOrEmpty(Code)) return string.Empty;
                var dashIndex = Code.IndexOf('-');
                return dashIndex > 0 && dashIndex < Code.Length - 1
                    ? Code.Substring(dashIndex + 1)
                    : string.Empty;
            }
        }

        /// <summary>
        /// Whether this locale has a region specified.
        /// </summary>
        public bool HasRegion => !string.IsNullOrEmpty(Region);

        /// <summary>
        /// Creates a new locale from an IETF language tag.
        /// </summary>
        /// <param name="code">The locale code (e.g., "en", "en-US", "tr").</param>
        public Locale(string code)
        {
            Code = code?.ToLowerInvariant() ?? string.Empty;
        }

        /// <summary>
        /// Returns the base language locale (e.g., "en" from "en-US").
        /// </summary>
        public Locale GetLanguageLocale()
        {
            return HasRegion ? new Locale(Language) : this;
        }

        public bool Equals(Locale other) => string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);
        public override bool Equals(object obj) => obj is Locale other && Equals(other);
        public override int GetHashCode() => Code?.GetHashCode() ?? 0;
        public override string ToString() => Code ?? string.Empty;

        public static bool operator ==(Locale left, Locale right) => left.Equals(right);
        public static bool operator !=(Locale left, Locale right) => !left.Equals(right);

        public static implicit operator string(Locale locale) => locale.Code;
        public static implicit operator Locale(string code) => new Locale(code);

        /// <summary>
        /// An empty/invalid locale.
        /// </summary>
        public static readonly Locale Empty = new Locale(string.Empty);
    }
}
