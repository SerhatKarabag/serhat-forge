using System;
using System.Collections.Generic;
using Serhat.Localization.Pluralization;

namespace Serhat.Localization.Data
{
    /// <summary>
    /// A table of localized strings for a single locale.
    /// </summary>
    [Serializable]
    public class StringTable
    {
        private readonly Dictionary<string, StringEntry> _entries = new Dictionary<string, StringEntry>();
        private readonly string _locale;

        /// <summary>
        /// The locale this table is for.
        /// </summary>
        public string Locale => _locale;

        /// <summary>
        /// The number of entries in this table.
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// All keys in this table.
        /// </summary>
        public IEnumerable<string> Keys => _entries.Keys;

        public StringTable(string locale)
        {
            _locale = locale ?? throw new ArgumentNullException(nameof(locale));
        }

        /// <summary>
        /// Adds or updates a simple string entry.
        /// </summary>
        public void SetString(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            _entries[key] = new StringEntry(value);
        }

        /// <summary>
        /// Adds or updates a plural entry.
        /// </summary>
        public void SetPluralEntry(string key, Dictionary<PluralCategory, string> pluralForms)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            _entries[key] = new StringEntry(pluralForms);
        }

        /// <summary>
        /// Gets a string value by key.
        /// </summary>
        public string GetString(string key)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                return entry.Value;
            }
            return null;
        }

        /// <summary>
        /// Gets an entry by key.
        /// </summary>
        public StringEntry GetEntry(string key)
        {
            _entries.TryGetValue(key, out var entry);
            return entry;
        }

        /// <summary>
        /// Checks if a key exists.
        /// </summary>
        public bool ContainsKey(string key)
        {
            return _entries.ContainsKey(key);
        }

        /// <summary>
        /// Removes an entry.
        /// </summary>
        public bool Remove(string key)
        {
            return _entries.Remove(key);
        }

        /// <summary>
        /// Clears all entries.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
        }
    }
}
