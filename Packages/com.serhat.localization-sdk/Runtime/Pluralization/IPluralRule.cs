namespace Serhat.Localization.Pluralization
{
    /// <summary>
    /// Interface for language-specific plural rules.
    /// </summary>
    public interface IPluralRule
    {
        /// <summary>
        /// The language code this rule applies to (e.g., "en", "tr", "ru").
        /// </summary>
        string LanguageCode { get; }

        /// <summary>
        /// Gets the plural category for a given number.
        /// </summary>
        /// <param name="number">The number to evaluate.</param>
        /// <returns>The plural category.</returns>
        PluralCategory GetCategory(decimal number);
    }
}
