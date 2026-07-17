using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serhat.Localization.Data;
using Serhat.Localization.Pluralization;
using Serhat.Localization.Utilities;
using UnityEngine;

namespace Serhat.Localization.Providers
{
    /// <summary>
    /// Loads localization data from Resources folder.
    /// </summary>
    public class ResourcesProvider : ILocalizationProvider
    {
        private readonly string _basePath;
        private StringTable _table;
        private bool _isInitialized;

        public bool IsInitialized => _isInitialized;

        public ResourcesProvider(string basePath)
        {
            _basePath = basePath ?? "Localization/Locales";
        }

        public Task InitializeAsync(string locale)
        {
            var resourcePath = $"{_basePath}/{locale}";
            var textAsset = Resources.Load<TextAsset>(resourcePath);

            if (textAsset == null)
            {
                Debug.LogWarning($"[Localization] Resource not found: {resourcePath}");
                _table = new StringTable(locale);
                _isInitialized = true;
                return Task.CompletedTask;
            }

            ParseJson(locale, textAsset.text);
            Resources.UnloadAsset(textAsset);

            _isInitialized = true;
            return Task.CompletedTask;
        }

        private void ParseJson(string locale, string json)
        {
            _table = new StringTable(locale);

            if (string.IsNullOrEmpty(json))
                return;

            try
            {
                var wrapper = JsonParser.ParseLocalizationJson(json);

                foreach (var kvp in wrapper.Entries)
                {
                    if (kvp.Value.IsPluralEntry)
                    {
                        _table.SetPluralEntry(kvp.Key, ConvertToPluralDictionary(kvp.Value));
                    }
                    else
                    {
                        _table.SetString(kvp.Key, kvp.Value.SimpleValue);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Localization] Failed to parse JSON for locale '{locale}': {ex.Message}");
            }
        }

        private Dictionary<PluralCategory, string> ConvertToPluralDictionary(JsonParser.LocalizationEntry entry)
        {
            var dict = new Dictionary<PluralCategory, string>();

            if (!string.IsNullOrEmpty(entry.Zero)) dict[PluralCategory.Zero] = entry.Zero;
            if (!string.IsNullOrEmpty(entry.One)) dict[PluralCategory.One] = entry.One;
            if (!string.IsNullOrEmpty(entry.Two)) dict[PluralCategory.Two] = entry.Two;
            if (!string.IsNullOrEmpty(entry.Few)) dict[PluralCategory.Few] = entry.Few;
            if (!string.IsNullOrEmpty(entry.Many)) dict[PluralCategory.Many] = entry.Many;
            if (!string.IsNullOrEmpty(entry.Other)) dict[PluralCategory.Other] = entry.Other;

            return dict;
        }

        public string GetString(string key)
        {
            return _table?.GetString(key);
        }

        public StringEntry GetEntry(string key)
        {
            return _table?.GetEntry(key);
        }

        public IEnumerable<string> GetAllKeys()
        {
            return _table?.Keys ?? Array.Empty<string>();
        }
    }
}
