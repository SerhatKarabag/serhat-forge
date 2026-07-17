using System.Collections.Generic;

namespace Serhat.Localization.Editor.Import
{
    /// <summary>
    /// Result of an import operation.
    /// </summary>
    public class ImportResult
    {
        public bool Success { get; set; }
        public string SourceFile { get; set; }
        public int KeyCount { get; set; }
        public int LocaleCount { get; set; }
        public List<string> Locales { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<string> GeneratedFiles { get; } = new List<string>();

        public void AddWarning(string message)
        {
            Warnings.Add(message);
        }

        public void AddError(string message)
        {
            Errors.Add(message);
            Success = false;
        }
    }
}
