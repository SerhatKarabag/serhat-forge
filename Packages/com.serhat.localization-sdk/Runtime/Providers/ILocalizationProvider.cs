using System.Collections.Generic;
using System.Threading.Tasks;
using Serhat.Localization.Data;

namespace Serhat.Localization.Providers
{
    /// <summary>
    /// Interface for localization data providers.
    /// </summary>
    public interface ILocalizationProvider
    {
        /// <summary>
        /// Initializes the provider for a specific locale.
        /// </summary>
        /// <param name="locale">The locale code.</param>
        Task InitializeAsync(string locale);

        /// <summary>
        /// Gets a localized string by key.
        /// </summary>
        /// <param name="key">The localization key.</param>
        /// <returns>The localized string, or null if not found.</returns>
        string GetString(string key);

        /// <summary>
        /// Gets a string entry (with plural support) by key.
        /// </summary>
        /// <param name="key">The localization key.</param>
        /// <returns>The string entry, or null if not found.</returns>
        StringEntry GetEntry(string key);

        /// <summary>
        /// Gets all available keys.
        /// </summary>
        IEnumerable<string> GetAllKeys();

        /// <summary>
        /// Whether the provider is initialized.
        /// </summary>
        bool IsInitialized { get; }
    }
}
