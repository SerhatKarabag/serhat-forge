using System;

namespace Serhat.Forge.FeatureGates
{
    public enum FeatureGateConditionRequirement : byte
    {
        Ignore = 0,
        RequireTrue = 1,
        RequireFalse = 2,
    }

    public interface IFeatureProgressProvider
    {
        int CurrentProgress { get; }
        event Action<int> ProgressChanged;
    }

    public interface IFeatureGateConditionProvider
    {
        event Action ConditionsChanged;
        bool TryGetCondition(string key, out bool value);
    }

    public interface IFeatureGateStateStore
    {
        bool IsSeen(FeatureId featureId);
        void SetSeen(FeatureId featureId, bool isSeen);
        void Flush();
    }

    public interface IFeatureGateService : IDisposable
    {
        event Action<FeatureId, FeatureGateState> StateChanged;
        FeatureGateState GetState(FeatureId featureId);
        bool TryGetState(FeatureId featureId, out FeatureGateState state);
        bool MarkSeen(FeatureId featureId);
        bool ClearSeen(FeatureId featureId);
        bool SetRuntimeOverride(FeatureId featureId, FeatureGateOverride runtimeOverride);
        bool ClearRuntimeOverride(FeatureId featureId);
        bool Refresh(FeatureId featureId);
        void RefreshAll();
    }

    public readonly struct FeatureGateOverride : IEquatable<FeatureGateOverride>
    {
        private const byte UnlockFlag = 1 << 0;
        private const byte VisibilityFlag = 1 << 1;

        private readonly byte _flags;
        private readonly bool _isUnlocked;
        private readonly bool _isVisible;

        public FeatureGateOverride(
            bool hasUnlockOverride,
            bool isUnlocked,
            bool hasVisibilityOverride,
            bool isVisible)
        {
            _flags = 0;
            if (hasUnlockOverride)
                _flags |= UnlockFlag;
            if (hasVisibilityOverride)
                _flags |= VisibilityFlag;

            _isUnlocked = isUnlocked;
            _isVisible = isVisible;
        }

        public bool HasAny => _flags != 0;
        public bool HasUnlockOverride => (_flags & UnlockFlag) != 0;
        public bool HasVisibilityOverride => (_flags & VisibilityFlag) != 0;
        public bool IsUnlocked => _isUnlocked;
        public bool IsVisible => _isVisible;

        public static FeatureGateOverride ForUnlock(bool value) =>
            new FeatureGateOverride(true, value, false, false);

        public static FeatureGateOverride ForVisibility(bool value) =>
            new FeatureGateOverride(false, false, true, value);

        public static FeatureGateOverride ForState(bool isUnlocked, bool isVisible) =>
            new FeatureGateOverride(true, isUnlocked, true, isVisible);

        public bool Equals(FeatureGateOverride other) =>
            _flags == other._flags
            && _isUnlocked == other._isUnlocked
            && _isVisible == other._isVisible;

        public override bool Equals(object obj) => obj is FeatureGateOverride other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)_flags;
                hash = (hash * 397) ^ (_isUnlocked ? 1 : 0);
                return (hash * 397) ^ (_isVisible ? 1 : 0);
            }
        }
    }

    /// <summary>Small adapter for projects that do not yet have a progression service.</summary>
    public sealed class MutableFeatureProgressProvider : IFeatureProgressProvider
    {
        private int _currentProgress;

        public MutableFeatureProgressProvider(int initialProgress = 0)
        {
            _currentProgress = initialProgress;
        }

        public int CurrentProgress => _currentProgress;
        public event Action<int> ProgressChanged;

        public bool SetProgress(int progress)
        {
            if (_currentProgress == progress)
                return false;

            _currentProgress = progress;
            ProgressChanged?.Invoke(progress);
            return true;
        }
    }
}
