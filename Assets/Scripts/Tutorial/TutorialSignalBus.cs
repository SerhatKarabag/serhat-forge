using System;
using System.Collections.Generic;

namespace Serhat.Forge.Tutorial
{
    /// <summary>
    /// Lightweight signal bus that decouples tutorial step completion from gameplay.
    /// Gameplay code calls <see cref="Raise"/>; the runner subscribes via <see cref="OnSignal"/>.
    /// </summary>
    public static class TutorialSignalBus
    {
        public static event Action<string> OnSignal;

        private static readonly HashSet<string> SessionSignals = new HashSet<string>(StringComparer.Ordinal);

        public static void Raise(string signal)
        {
            if (string.IsNullOrEmpty(signal))
                return;

            SessionSignals.Add(signal);
            OnSignal?.Invoke(signal);
        }

        /// <summary>True if Raise(signal) was called at least once this session.</summary>
        public static bool HasFired(string signal) => !string.IsNullOrEmpty(signal) && SessionSignals.Contains(signal);

        /// <summary>Clear session signals (e.g. on level reload).</summary>
        public static void ClearSession() => SessionSignals.Clear();
    }
}
