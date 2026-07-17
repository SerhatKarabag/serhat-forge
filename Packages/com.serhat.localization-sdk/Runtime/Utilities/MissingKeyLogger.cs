using System.Collections.Generic;
using UnityEngine;

namespace Serhat.Localization.Utilities
{
    /// <summary>
    /// Rate-limited logger for missing localization keys.
    /// Logs each missing key only once per session.
    /// </summary>
    public class MissingKeyLogger
    {
        private readonly HashSet<string> _loggedKeys = new HashSet<string>();
        private readonly object _lock = new object();

        /// <summary>
        /// Logs a missing key (only once per key per session).
        /// </summary>
        public void LogMissingKey(string key, string locale)
        {
            var cacheKey = $"{locale}:{key}";

            lock (_lock)
            {
                if (_loggedKeys.Contains(cacheKey))
                    return;

                _loggedKeys.Add(cacheKey);
            }

            Debug.LogWarning($"[Localization] Missing key '{key}' for locale '{locale}'");
        }

        /// <summary>
        /// Clears the logged keys cache.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _loggedKeys.Clear();
            }
        }

        /// <summary>
        /// Gets the number of unique missing keys logged.
        /// </summary>
        public int MissingKeyCount
        {
            get
            {
                lock (_lock)
                {
                    return _loggedKeys.Count;
                }
            }
        }
    }
}
