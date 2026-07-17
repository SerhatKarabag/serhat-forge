using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Serhat.Forge.FeatureGates
{
    /// <summary>Generic progress-threshold catalog. The type name is retained for asset compatibility.</summary>
    [CreateAssetMenu(fileName = "LevelUnlockCatalog", menuName = "Serhat Forge/Config/Progress Unlock Catalog")]
    public sealed class LevelUnlockCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField, FormerlySerializedAs("FeatureId")]
            private FeatureId _featureId = FeatureId.None;

            [SerializeField, Min(0), FormerlySerializedAs("UnlockLevel")]
            private int _progressThreshold = 1;

            [SerializeField, Min(0), FormerlySerializedAs("SourceLevel")]
            [Tooltip("Optional source progress value used to generate this entry.")]
            private int _sourceProgress;

            public FeatureId FeatureId => _featureId;
            public int ProgressThreshold => Mathf.Max(0, _progressThreshold);
            public int SourceProgress => Mathf.Max(0, _sourceProgress);
            public int UnlockLevel => ProgressThreshold;
            public int SourceLevel => SourceProgress;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        public Entry[] Entries => _entries;

        public void ValidateOrThrow()
        {
            var entries = _entries;
            if (entries == null)
                return;

            for (var i = 0; i < entries.Length; i++)
            {
                var featureId = entries[i]?.FeatureId ?? FeatureId.None;
                if (featureId == FeatureId.None)
                    continue;

                for (var j = i + 1; j < entries.Length; j++)
                {
                    if (entries[j] == null || entries[j].FeatureId != featureId)
                        continue;

                    throw new InvalidOperationException(
                        $"LevelUnlockCatalog contains duplicate FeatureId '{featureId}' " +
                        $"at entry indexes {i} and {j}.");
                }
            }
        }

        public bool TryGetProgressThreshold(FeatureId featureId, out int progressThreshold)
        {
            ValidateOrThrow();
            var entries = _entries;
            if (entries != null)
            {
                for (var i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    if (entry == null || entry.FeatureId != featureId)
                        continue;

                    progressThreshold = entry.ProgressThreshold;
                    return true;
                }
            }

            progressThreshold = 0;
            return false;
        }

        public bool TryGetUnlockLevel(FeatureId featureId, out int unlockLevel) =>
            TryGetProgressThreshold(featureId, out unlockLevel);

#if UNITY_EDITOR
        public void SetEntries(Entry[] entries)
        {
            _entries = entries ?? Array.Empty<Entry>();
        }
#endif
    }
}
