using System;
using System.Collections.Generic;
using System.Text;

namespace Serhat.Localization.Utilities
{
    /// <summary>
    /// Custom JSON parser for localization files.
    /// Handles both simple strings and plural objects without external dependencies.
    /// </summary>
    public static class JsonParser
    {
        /// <summary>
        /// Represents a parsed localization entry.
        /// </summary>
        public class LocalizationEntry
        {
            public string SimpleValue { get; set; }
            public string Zero { get; set; }
            public string One { get; set; }
            public string Two { get; set; }
            public string Few { get; set; }
            public string Many { get; set; }
            public string Other { get; set; }

            public bool IsPluralEntry => !string.IsNullOrEmpty(Zero) || !string.IsNullOrEmpty(One) ||
                                         !string.IsNullOrEmpty(Two) || !string.IsNullOrEmpty(Few) ||
                                         !string.IsNullOrEmpty(Many) || !string.IsNullOrEmpty(Other);
        }

        /// <summary>
        /// Wrapper for parsed JSON.
        /// </summary>
        public class LocalizationData
        {
            public Dictionary<string, LocalizationEntry> Entries { get; } = new Dictionary<string, LocalizationEntry>();
        }

        /// <summary>
        /// Parses localization JSON into a structured format.
        /// </summary>
        public static LocalizationData ParseLocalizationJson(string json)
        {
            var data = new LocalizationData();

            if (string.IsNullOrEmpty(json))
                return data;

            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}"))
                return data;

            // Remove outer braces
            json = json.Substring(1, json.Length - 2).Trim();

            var entries = ParseObject(json);
            foreach (var kvp in entries)
            {
                data.Entries[kvp.Key] = kvp.Value;
            }

            return data;
        }

        private static Dictionary<string, LocalizationEntry> ParseObject(string json)
        {
            var result = new Dictionary<string, LocalizationEntry>();
            int index = 0;

            while (index < json.Length)
            {
                // Skip whitespace
                SkipWhitespace(json, ref index);
                if (index >= json.Length) break;

                // Parse key
                if (json[index] != '"')
                {
                    index++;
                    continue;
                }

                var key = ParseString(json, ref index);

                // Skip to colon
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != ':')
                {
                    continue;
                }
                index++; // Skip colon
                SkipWhitespace(json, ref index);

                if (index >= json.Length) break;

                // Parse value
                LocalizationEntry entry;
                if (json[index] == '"')
                {
                    // Simple string value
                    var value = ParseString(json, ref index);
                    entry = new LocalizationEntry { SimpleValue = value };
                }
                else if (json[index] == '{')
                {
                    // Plural object
                    entry = ParsePluralObject(json, ref index);
                }
                else
                {
                    // Skip unknown value
                    SkipValue(json, ref index);
                    continue;
                }

                result[key] = entry;

                // Skip comma
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                {
                    index++;
                }
            }

            return result;
        }

        private static LocalizationEntry ParsePluralObject(string json, ref int index)
        {
            var entry = new LocalizationEntry();

            if (json[index] != '{')
                return entry;

            index++; // Skip opening brace

            while (index < json.Length && json[index] != '}')
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] == '}') break;

                if (json[index] != '"')
                {
                    index++;
                    continue;
                }

                var pluralKey = ParseString(json, ref index);

                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != ':')
                {
                    continue;
                }
                index++; // Skip colon
                SkipWhitespace(json, ref index);

                if (index >= json.Length || json[index] != '"')
                {
                    SkipValue(json, ref index);
                    continue;
                }

                var value = ParseString(json, ref index);

                switch (pluralKey.ToLowerInvariant())
                {
                    case "zero": entry.Zero = value; break;
                    case "one": entry.One = value; break;
                    case "two": entry.Two = value; break;
                    case "few": entry.Few = value; break;
                    case "many": entry.Many = value; break;
                    case "other": entry.Other = value; break;
                }

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                {
                    index++;
                }
            }

            if (index < json.Length && json[index] == '}')
            {
                index++;
            }

            return entry;
        }

        private static string ParseString(string json, ref int index)
        {
            if (json[index] != '"')
                return string.Empty;

            index++; // Skip opening quote
            var sb = new StringBuilder();

            while (index < json.Length)
            {
                char c = json[index];

                if (c == '"')
                {
                    index++;
                    break;
                }

                if (c == '\\' && index + 1 < json.Length)
                {
                    index++;
                    char escaped = json[index];
                    switch (escaped)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 < json.Length)
                            {
                                var hex = json.Substring(index + 1, 4);
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                                {
                                    sb.Append((char)code);
                                    index += 4;
                                }
                            }
                            break;
                        default: sb.Append(escaped); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
                index++;
            }

            return sb.ToString();
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }
        }

        private static void SkipValue(string json, ref int index)
        {
            int depth = 0;
            while (index < json.Length)
            {
                char c = json[index];
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    if (depth == 0) return;
                    depth--;
                }
                else if ((c == ',' || c == '}' || c == ']') && depth == 0)
                {
                    return;
                }
                index++;
            }
        }

        /// <summary>
        /// Converts a LocalizationData back to JSON string.
        /// </summary>
        public static string ToJson(LocalizationData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");

            var keys = new List<string>(data.Entries.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                var entry = data.Entries[key];

                sb.Append($"  \"{EscapeString(key)}\": ");

                if (entry.IsPluralEntry)
                {
                    sb.AppendLine("{");
                    var pluralParts = new List<string>();

                    if (!string.IsNullOrEmpty(entry.Zero)) pluralParts.Add($"    \"zero\": \"{EscapeString(entry.Zero)}\"");
                    if (!string.IsNullOrEmpty(entry.One)) pluralParts.Add($"    \"one\": \"{EscapeString(entry.One)}\"");
                    if (!string.IsNullOrEmpty(entry.Two)) pluralParts.Add($"    \"two\": \"{EscapeString(entry.Two)}\"");
                    if (!string.IsNullOrEmpty(entry.Few)) pluralParts.Add($"    \"few\": \"{EscapeString(entry.Few)}\"");
                    if (!string.IsNullOrEmpty(entry.Many)) pluralParts.Add($"    \"many\": \"{EscapeString(entry.Many)}\"");
                    if (!string.IsNullOrEmpty(entry.Other)) pluralParts.Add($"    \"other\": \"{EscapeString(entry.Other)}\"");

                    sb.AppendLine(string.Join(",\n", pluralParts));
                    sb.Append("  }");
                }
                else
                {
                    sb.Append($"\"{EscapeString(entry.SimpleValue ?? "")}\"");
                }

                if (i < keys.Count - 1)
                {
                    sb.Append(",");
                }
                sb.AppendLine();
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
