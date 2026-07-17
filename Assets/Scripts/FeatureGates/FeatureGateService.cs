using System;
using System.Collections.Generic;

namespace Serhat.Forge.FeatureGates
{
    /// <summary>
    /// Main-thread feature-gate runtime. State evaluation is allocation-free after index construction.
    /// Changed-state publication snapshots subscribers so one failing handler cannot block the rest.
    /// </summary>
    public sealed class FeatureGateService : IFeatureGateService
    {
        private readonly IFeatureProgressProvider _progressProvider;
        private readonly IFeatureGateStateStore _stateStore;
        private readonly IFeatureGateConditionProvider _conditionProvider;
        private readonly Dictionary<FeatureId, FeatureGateConfig.Rule> _rules =
            new Dictionary<FeatureId, FeatureGateConfig.Rule>();
        private readonly Dictionary<FeatureId, int> _catalogThresholds =
            new Dictionary<FeatureId, int>();
        private readonly Dictionary<FeatureId, FeatureGateOverride> _runtimeOverrides =
            new Dictionary<FeatureId, FeatureGateOverride>();
        private readonly Dictionary<FeatureId, FeatureGateState> _states =
            new Dictionary<FeatureId, FeatureGateState>();
        private readonly HashSet<FeatureId> _knownIds = new HashSet<FeatureId>();
        private readonly HashSet<FeatureId> _seenIds = new HashSet<FeatureId>();
        private readonly List<FeatureId> _featureIds = new List<FeatureId>();

        private bool _initialized;
        private bool _disposed;

        public FeatureGateService(
            FeatureGateConfig config,
            LevelUnlockCatalog catalog,
            IFeatureProgressProvider progressProvider,
            IFeatureGateStateStore stateStore,
            IFeatureGateConditionProvider conditionProvider = null)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            config.ValidateOrThrow();
            catalog?.ValidateOrThrow();

            _progressProvider = progressProvider ?? throw new ArgumentNullException(nameof(progressProvider));
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _conditionProvider = conditionProvider;

            BuildIndexes(config, catalog);
            for (var i = 0; i < _featureIds.Count; i++)
                InitializeState(_featureIds[i]);

            _initialized = true;
            _progressProvider.ProgressChanged += OnProgressChanged;
            if (_conditionProvider != null)
                _conditionProvider.ConditionsChanged += OnConditionsChanged;
        }

        public event Action<FeatureId, FeatureGateState> StateChanged;

        public FeatureGateState GetState(FeatureId featureId) =>
            _states.TryGetValue(featureId, out var state) ? state : FeatureGateState.Unavailable;

        public bool TryGetState(FeatureId featureId, out FeatureGateState state) =>
            _states.TryGetValue(featureId, out state);

        public bool MarkSeen(FeatureId featureId) => SetSeen(featureId, true);
        public bool ClearSeen(FeatureId featureId) => SetSeen(featureId, false);

        public bool SetRuntimeOverride(FeatureId featureId, FeatureGateOverride runtimeOverride)
        {
            ThrowIfDisposed();
            if (featureId == FeatureId.None)
                return false;
            if (!runtimeOverride.HasAny)
                return ClearRuntimeOverride(featureId);

            AddKnown(featureId);
            if (_runtimeOverrides.TryGetValue(featureId, out var current) && current.Equals(runtimeOverride))
                return false;

            _runtimeOverrides[featureId] = runtimeOverride;
            EvaluateAndPublish(featureId);
            return true;
        }

        public bool ClearRuntimeOverride(FeatureId featureId)
        {
            ThrowIfDisposed();
            if (!_runtimeOverrides.Remove(featureId))
                return false;

            EvaluateAndPublish(featureId);
            return true;
        }

        public bool Refresh(FeatureId featureId)
        {
            ThrowIfDisposed();
            if (!_knownIds.Contains(featureId))
                return false;

            EvaluateAndPublish(featureId);
            return true;
        }

        public void RefreshAll()
        {
            ThrowIfDisposed();
            var count = _featureIds.Count;
            for (var i = 0; i < count; i++)
                EvaluateAndPublish(_featureIds[i]);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _progressProvider.ProgressChanged -= OnProgressChanged;
            if (_conditionProvider != null)
                _conditionProvider.ConditionsChanged -= OnConditionsChanged;

            _stateStore.Flush();
            StateChanged = null;
        }

        private void BuildIndexes(FeatureGateConfig config, LevelUnlockCatalog catalog)
        {
            var rules = config.Rules;
            if (rules != null)
            {
                for (var i = 0; i < rules.Length; i++)
                {
                    var rule = rules[i];
                    if (rule == null || rule.FeatureId == FeatureId.None)
                        continue;

                    _rules.Add(rule.FeatureId, rule);
                    AddKnown(rule.FeatureId);
                }
            }

            if (catalog == null || catalog.Entries == null)
                return;

            var entries = catalog.Entries;
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.FeatureId == FeatureId.None)
                    continue;

                _catalogThresholds.Add(entry.FeatureId, entry.ProgressThreshold);
                AddKnown(entry.FeatureId);
            }
        }

        private void AddKnown(FeatureId featureId)
        {
            if (featureId == FeatureId.None || !_knownIds.Add(featureId))
                return;

            _featureIds.Add(featureId);
            if (_initialized)
                InitializeState(featureId);
        }

        private void InitializeState(FeatureId featureId)
        {
            if (_stateStore.IsSeen(featureId))
                _seenIds.Add(featureId);

            _states[featureId] = Evaluate(featureId);
        }

        private bool SetSeen(FeatureId featureId, bool isSeen)
        {
            ThrowIfDisposed();
            if (!_knownIds.Contains(featureId))
                return false;

            var changed = isSeen ? _seenIds.Add(featureId) : _seenIds.Remove(featureId);
            if (!changed)
                return false;

            try
            {
                _stateStore.SetSeen(featureId, isSeen);
            }
            catch
            {
                if (isSeen)
                    _seenIds.Remove(featureId);
                else
                    _seenIds.Add(featureId);
                throw;
            }

            EvaluateAndPublish(featureId);
            return true;
        }

        private FeatureGateState Evaluate(FeatureId featureId)
        {
            var hasRule = _rules.TryGetValue(featureId, out var rule);
            var hasCatalog = _catalogThresholds.TryGetValue(featureId, out var catalogThreshold);
            var isConfigured = hasRule || hasCatalog;
            var threshold = hasCatalog
                ? catalogThreshold
                : hasRule ? rule.ProgressThreshold : int.MaxValue;

            var isUnlocked = isConfigured && _progressProvider.CurrentProgress >= threshold;
            var isVisible = isConfigured;
            var isSeen = _seenIds.Contains(featureId);

            if (hasRule &&
                (rule.UnlockCondition != FeatureGateConditionRequirement.Ignore ||
                 rule.VisibilityCondition != FeatureGateConditionRequirement.Ignore))
            {
                var hasCondition = false;
                var conditionValue = false;
                if (_conditionProvider != null && !string.IsNullOrEmpty(rule.ExternalConditionKey))
                {
                    hasCondition = _conditionProvider.TryGetCondition(
                        rule.ExternalConditionKey,
                        out conditionValue);
                }

                isUnlocked &= IsConditionSatisfied(rule.UnlockCondition, hasCondition, conditionValue);
                isVisible &= IsConditionSatisfied(rule.VisibilityCondition, hasCondition, conditionValue);
            }

            if (_runtimeOverrides.TryGetValue(featureId, out var runtimeOverride))
            {
                if (runtimeOverride.HasUnlockOverride)
                    isUnlocked = runtimeOverride.IsUnlocked;
                if (runtimeOverride.HasVisibilityOverride)
                    isVisible = runtimeOverride.IsVisible;
            }

            var showNotification = hasRule
                                   && rule.EnableNotification
                                   && isUnlocked
                                   && isVisible
                                   && !isSeen;

            return new FeatureGateState(isUnlocked, isVisible, isSeen, showNotification, threshold);
        }

        private void EvaluateAndPublish(FeatureId featureId)
        {
            var next = Evaluate(featureId);
            if (_states.TryGetValue(featureId, out var current) && current.Equals(next))
                return;

            _states[featureId] = next;
            var handlers = StateChanged;
            if (handlers == null)
                return;

            foreach (Action<FeatureId, FeatureGateState> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(featureId, next);
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception);
                }
            }
        }

        private static bool IsConditionSatisfied(
            FeatureGateConditionRequirement requirement,
            bool hasCondition,
            bool value)
        {
            switch (requirement)
            {
                case FeatureGateConditionRequirement.Ignore:
                    return true;
                case FeatureGateConditionRequirement.RequireTrue:
                    return hasCondition && value;
                case FeatureGateConditionRequirement.RequireFalse:
                    return hasCondition && !value;
                default:
                    return false;
            }
        }

        private void OnProgressChanged(int _) => RefreshAll();
        private void OnConditionsChanged() => RefreshAll();

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FeatureGateService));
        }
    }
}
