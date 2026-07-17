using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Serhat.Localization.Data;
using Serhat.Localization.Pluralization;
using Serhat.Localization.Utilities;
using UnityEngine;
using UnityEngine.Networking;

namespace Serhat.Localization.Providers
{
    /// <summary>
    /// Loads localization data from StreamingAssets folder.
    /// </summary>
    public class StreamingAssetsProvider : ILocalizationProvider
    {
        private readonly string _basePath;
        private StringTable _table;
        private bool _isInitialized;

        public bool IsInitialized => _isInitialized;

        public StreamingAssetsProvider(string basePath)
        {
            _basePath = basePath ?? "Localization/Locales";
        }

        public async Task InitializeAsync(string locale)
        {
            var filePath = Path.Combine(Application.streamingAssetsPath, _basePath, $"{locale}.json");

            string json;

            // Use UnityWebRequest for Android/WebGL compatibility
            if (filePath.Contains("://") || filePath.Contains(":///"))
            {
                json = await LoadWithWebRequestAsync(filePath);
            }
            else
            {
                // Direct file access for editor/standalone
                if (File.Exists(filePath))
                {
                    json = await Task.Run(() => File.ReadAllText(filePath));
                }
                else
                {
                    Debug.LogWarning($"[Localization] File not found: {filePath}");
                    _table = new StringTable(locale);
                    _isInitialized = true;
                    return;
                }
            }

            ParseJson(locale, json);
            _isInitialized = true;
        }

        private async Task<string> LoadWithWebRequestAsync(string path)
        {
            using var request = UnityWebRequest.Get(path);
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Localization] Failed to load: {path} - {request.error}");
                return "{}";
            }

            return request.downloadHandler.text;
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
