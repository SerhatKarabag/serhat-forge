using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serhat.Localization.Utilities;
using UnityEngine;

namespace Serhat.Localization.Editor.Import
{
    /// <summary>
    /// Imports localization data from CSV files.
    /// </summary>
    public static class CsvImporter
    {
        /// <summary>
        /// Imports a CSV file and generates JSON files for each locale.
        /// </summary>
        /// <param name="csvPath">Path to the CSV file.</param>
        /// <param name="outputPath">Output directory for JSON files.</param>
        /// <param name="defaultLocale">The default locale code.</param>
        /// <returns>Import result with details.</returns>
        public static ImportResult Import(string csvPath, string outputPath, string defaultLocale = "en")
        {
            var result = new ImportResult
            {
                Success = true,
                SourceFile = csvPath
            };

            if (!File.Exists(csvPath))
            {
                result.AddError($"CSV file not found: {csvPath}");
                return result;
            }

            try
            {
                var lines = File.ReadAllLines(csvPath, Encoding.UTF8);
                if (lines.Length < 2)
                {
                    result.AddError("CSV file must have at least a header row and one data row.");
                    return result;
                }

                // Parse header
                var header = ParseCsvLine(lines[0]);
                if (header.Length < 2)
                {
                    result.AddError("CSV header must have at least 'key' and one locale column.");
                    return result;
                }

                // First column is 'key', rest are locales
                var locales = new List<string>();
                for (int i = 1; i < header.Length; i++)
                {
                    var locale = header[i].Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(locale))
                    {
                        locales.Add(locale);
                        result.Locales.Add(locale);
                    }
                }

                result.LocaleCount = locales.Count;

                // Check if default locale exists
                if (!locales.Contains(defaultLocale.ToLowerInvariant()))
                {
                    result.AddError($"Default locale '{defaultLocale}' column not found in CSV.");
                    return result;
                }

                // Parse data rows - per locale data
                var data = new Dictionary<string, Dictionary<string, string>>();
                var pluralData = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                foreach (var locale in locales)
                {
                    data[locale] = new Dictionary<string, string>();
                    pluralData[locale] = new Dictionary<string, Dictionary<string, string>>();
                }

                for (int lineNum = 1; lineNum < lines.Length; lineNum++)
                {
                    var line = lines[lineNum].Trim();

                    // Skip empty lines and comments
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    var values = ParseCsvLine(line);
                    if (values.Length < 1)
                        continue;

                    var key = values[0].Trim();
                    if (string.IsNullOrEmpty(key))
                        continue;

                    // Check for plural suffix
                    string baseKey = key;
                    string pluralForm = null;
                    var pluralSuffixes = new[] { ".zero", ".one", ".two", ".few", ".many", ".other" };

                    foreach (var suffix in pluralSuffixes)
                    {
                        if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        {
                            baseKey = key.Substring(0, key.Length - suffix.Length);
                            pluralForm = suffix.Substring(1); // Remove leading dot
                            break;
                        }
                    }

                    for (int i = 0; i < locales.Count && i + 1 < values.Length; i++)
                    {
                        var locale = locales[i];
                        var value = values[i + 1].Trim();

                        if (pluralForm != null)
                        {
                            // Plural entry
                            if (!pluralData[locale].ContainsKey(baseKey))
                                pluralData[locale][baseKey] = new Dictionary<string, string>();

                            pluralData[locale][baseKey][pluralForm] = value;
                        }
                        else
                        {
                            // Simple entry
                            data[locale][key] = value;
                        }
                    }
                }

                // Count keys
                var uniqueKeys = new HashSet<string>();
                foreach (var locale in locales)
                {
                    foreach (var key in data[locale].Keys)
                        uniqueKeys.Add(key);
                    foreach (var key in pluralData[locale].Keys)
                        uniqueKeys.Add(key);
                }
                result.KeyCount = uniqueKeys.Count;

                // Create output directory
                if (!Directory.Exists(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                }

                // Generate JSON files
                foreach (var locale in locales)
                {
                    var locData = new JsonParser.LocalizationData();

                    // Add simple entries
                    foreach (var kvp in data[locale])
                    {
                        locData.Entries[kvp.Key] = new JsonParser.LocalizationEntry
                        {
                            SimpleValue = kvp.Value
                        };
                    }

                    // Add plural entries
                    foreach (var kvp in pluralData[locale])
                    {
                        var entry = new JsonParser.LocalizationEntry();

                        if (kvp.Value.TryGetValue("zero", out var zero)) entry.Zero = zero;
                        if (kvp.Value.TryGetValue("one", out var one)) entry.One = one;
                        if (kvp.Value.TryGetValue("two", out var two)) entry.Two = two;
                        if (kvp.Value.TryGetValue("few", out var few)) entry.Few = few;
                        if (kvp.Value.TryGetValue("many", out var many)) entry.Many = many;
                        if (kvp.Value.TryGetValue("other", out var other)) entry.Other = other;

                        locData.Entries[kvp.Key] = entry;
                    }

                    var json = JsonParser.ToJson(locData);
                    var outputFile = Path.Combine(outputPath, $"{locale}.json");
                    File.WriteAllText(outputFile, json, Encoding.UTF8);
                    result.GeneratedFiles.Add(outputFile);
                }

                return result;
            }
            catch (Exception ex)
            {
                result.AddError($"Failed to import CSV: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Parses a CSV line, handling quoted values.
        /// </summary>
        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        // Escaped quote
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
