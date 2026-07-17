using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Serhat.Forge.FeatureGates
{
    [CreateAssetMenu(fileName = "FeatureGateConfig", menuName = "Serhat Forge/Config/Feature Gate Config")]
    public sealed class FeatureGateConfig : ScriptableObject
    {
        [Serializable]
        public sealed class Rule
        {
            private const string LegacyNoAdsEntitlementKey = "entitlement.no_ads";

            [SerializeField, FormerlySerializedAs("FeatureId")]
            private FeatureId _featureId = FeatureId.None;

            [SerializeField, Min(0), FormerlySerializedAs("UnlockLevel")]
            private int _progressThreshold = 1;

            [SerializeField, FormerlySerializedAs("EnableNotification")]
            [Tooltip("Show a notification while the feature is unlocked, visible and unseen.")]
            private bool _enableNotification = true;

            [SerializeField]
            [Tooltip("External condition key, such as entitlement.premium or experiment.shop.")]
            private string _externalConditionKey = string.Empty;

            [SerializeField] private FeatureGateConditionRequirement _unlockCondition;
            [SerializeField] private FeatureGateConditionRequirement _visibilityCondition;

            [SerializeField, HideInInspector, FormerlySerializedAs("HideWhenNoAdsOwned")]
            private bool _legacyHideWhenNoAdsOwned;

            public FeatureId FeatureId => _featureId;
            public int ProgressThreshold => Mathf.Max(0, _progressThreshold);
            public int UnlockLevel => ProgressThreshold;
            public bool EnableNotification => _enableNotification;

            public string ExternalConditionKey =>
                !string.IsNullOrEmpty(_externalConditionKey)
                    ? _externalConditionKey
                    : _legacyHideWhenNoAdsOwned ? LegacyNoAdsEntitlementKey : string.Empty;

            public FeatureGateConditionRequirement UnlockCondition => _unlockCondition;

            public FeatureGateConditionRequirement VisibilityCondition =>
                _visibilityCondition != FeatureGateConditionRequirement.Ignore
                    ? _visibilityCondition
                    : _legacyHideWhenNoAdsOwned
                        ? FeatureGateConditionRequirement.RequireFalse
                        : FeatureGateConditionRequirement.Ignore;
        }

        [SerializeField] private Rule[] _rules = Array.Empty<Rule>();

        public Rule[] Rules => _rules;

        public void ValidateOrThrow()
        {
            var rules = _rules;
            if (rules == null)
                return;

            for (var i = 0; i < rules.Length; i++)
            {
                var featureId = rules[i]?.FeatureId ?? FeatureId.None;
                if (featureId == FeatureId.None)
                    continue;

                for (var j = i + 1; j < rules.Length; j++)
                {
                    if (rules[j] == null || rules[j].FeatureId != featureId)
                        continue;

                    throw new InvalidOperationException(
                        $"FeatureGateConfig contains duplicate FeatureId '{featureId}' " +
                        $"at rule indexes {i} and {j}.");
                }
            }
        }

        public bool TryGetRule(FeatureId featureId, out Rule rule)
        {
            ValidateOrThrow();
            var rules = _rules;
            if (rules != null)
            {
                for (var i = 0; i < rules.Length; i++)
                {
                    var candidate = rules[i];
                    if (candidate == null || candidate.FeatureId != featureId)
                        continue;

                    rule = candidate;
                    return true;
                }
            }

            rule = null;
            return false;
        }
    }
}
