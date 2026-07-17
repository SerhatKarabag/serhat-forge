using System.Collections.Generic;

namespace Serhat.Forge.Content
{
    /// <summary>Common, game-agnostic Addressables labels.</summary>
    public static class ContentLabels
    {
        public const string Core = "core";
        public const string Gameplay = "gameplay";
        public const string UI = "ui";
        public const string Audio = "audio";

        private static readonly string[] LabelValues = { Core, Gameplay, UI, Audio };
        public static IReadOnlyList<string> All => LabelValues;
    }
}
