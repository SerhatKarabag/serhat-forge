using System;
using System.Collections.Generic;
using Serhat.Localization.Pluralization;

namespace Serhat.Localization.Data
{
    /// <summary>
    /// Represents a localization entry that can be either a simple string or plural forms.
    /// </summary>
    [Serializable]
    public class StringEntry
    {
        private string _simpleValue;
        private Dictionary<PluralCategory, string> _pluralForms;

        /// <summary>
        /// Whether this entry contains plural forms.
        /// </summary>
        public bool IsPluralEntry => _pluralForms != null && _pluralForms.Count > 0;

        /// <summary>
        /// Gets the simple value (for non-plural entries).
        /// </summary>
        public string Value => _simpleValue;

        /// <summary>
        /// Creates a simple string entry.
        /// </summary>
        public StringEntry(string value)
        {
            _simpleValue = value;
        }

        /// <summary>
        /// Creates a plural entry.
        /// </summary>
        public StringEntry(Dictionary<PluralCategory, string> pluralForms)
        {
            _pluralForms = pluralForms;
        }

        /// <summary>
        /// Gets the plural form for a category.
        /// </summary>
        public string GetPluralForm(PluralCategory category)
        {
            if (_pluralForms == null)
                return _simpleValue;

            if (_pluralForms.TryGetValue(category, out var value))
                return value;

            // Fall back to "other"
            if (_pluralForms.TryGetValue(PluralCategory.Other, out value))
                return value;

            return _simpleValue;
        }

        /// <summary>
        /// Sets a plural form.
        /// </summary>
        public void SetPluralForm(PluralCategory category, string value)
        {
            _pluralForms ??= new Dictionary<PluralCategory, string>();
            _pluralForms[category] = value;
        }

        public override string ToString()
        {
            return IsPluralEntry ? $"[Plural: {_pluralForms.Count} forms]" : _simpleValue ?? string.Empty;
        }
    }
}
