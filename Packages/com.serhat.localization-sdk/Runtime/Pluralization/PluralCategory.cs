namespace Serhat.Localization.Pluralization
{
    /// <summary>
    /// CLDR plural categories.
    /// </summary>
    public enum PluralCategory
    {
        /// <summary>
        /// Used for zero quantity in some languages (e.g., Arabic).
        /// </summary>
        Zero,

        /// <summary>
        /// Used for singular (e.g., 1 item).
        /// </summary>
        One,

        /// <summary>
        /// Used for dual in some languages (e.g., Arabic).
        /// </summary>
        Two,

        /// <summary>
        /// Used for few (e.g., 2-4 in Russian).
        /// </summary>
        Few,

        /// <summary>
        /// Used for many (e.g., 5-20 in Russian).
        /// </summary>
        Many,

        /// <summary>
        /// Default/other category.
        /// </summary>
        Other
    }
}
