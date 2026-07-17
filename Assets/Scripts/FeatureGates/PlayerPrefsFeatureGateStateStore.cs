using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Serhat.Forge.FeatureGates
{
    /// <summary>Sparse seen-state persistence: one key per stable numeric FeatureId.</summary>
    public sealed class PlayerPrefsFeatureGateStateStore : IFeatureGateStateStore
    {
        private const string DefaultPrefix = "serhatforge.featureGate.seen.";

        private readonly string _keyPrefix;
        private readonly bool _saveImmediately;
        private readonly Dictionary<FeatureId, string> _keyCache =
            new Dictionary<FeatureId, string>();
        private bool _isDirty;

        public PlayerPrefsFeatureGateStateStore(
            string keyPrefix = DefaultPrefix,
            bool saveImmediately = true)
        {
            if (string.IsNullOrWhiteSpace(keyPrefix))
                throw new ArgumentException("A non-empty key prefix is required.", nameof(keyPrefix));

            _keyPrefix = keyPrefix;
            _saveImmediately = saveImmediately;
        }

        public bool IsSeen(FeatureId featureId) => PlayerPrefs.GetInt(GetKey(featureId), 0) == 1;

        public void SetSeen(FeatureId featureId, bool isSeen)
        {
            var key = GetKey(featureId);
            if (isSeen)
                PlayerPrefs.SetInt(key, 1);
            else
                PlayerPrefs.DeleteKey(key);

            _isDirty = true;
            if (_saveImmediately)
                Flush();
        }

        public void Flush()
        {
            if (!_isDirty)
                return;

            PlayerPrefs.Save();
            _isDirty = false;
        }

        private string GetKey(FeatureId featureId)
        {
            if (_keyCache.TryGetValue(featureId, out var key))
                return key;

            key = _keyPrefix + ((int)featureId).ToString(CultureInfo.InvariantCulture);
            _keyCache.Add(featureId, key);
            return key;
        }
    }
}
