namespace Serhat.Forge.FeatureGates
{
    public readonly struct FeatureGateState : System.IEquatable<FeatureGateState>
    {
        public static FeatureGateState Unavailable =>
            new FeatureGateState(false, false, false, false, int.MaxValue);

        public FeatureGateState(bool isUnlocked, bool isVisible, bool showNotification, int unlockLevel)
            : this(isUnlocked, isVisible, false, showNotification, unlockLevel)
        {
        }

        public FeatureGateState(
            bool isUnlocked,
            bool isVisible,
            bool isSeen,
            bool showNotification,
            int progressThreshold)
        {
            IsUnlocked = isUnlocked;
            IsVisible = isVisible;
            IsSeen = isSeen;
            ShowNotification = showNotification;
            ProgressThreshold = progressThreshold;
        }

        public bool IsUnlocked { get; }
        public bool IsVisible { get; }
        public bool IsSeen { get; }
        public bool ShowNotification { get; }
        public int ProgressThreshold { get; }
        public int UnlockLevel => ProgressThreshold;

        public bool Equals(FeatureGateState other)
        {
            return IsUnlocked == other.IsUnlocked
                   && IsVisible == other.IsVisible
                   && IsSeen == other.IsSeen
                   && ShowNotification == other.ShowNotification
                   && ProgressThreshold == other.ProgressThreshold;
        }

        public override bool Equals(object obj) => obj is FeatureGateState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = IsUnlocked ? 1 : 0;
                hash = (hash * 397) ^ (IsVisible ? 1 : 0);
                hash = (hash * 397) ^ (IsSeen ? 1 : 0);
                hash = (hash * 397) ^ (ShowNotification ? 1 : 0);
                return (hash * 397) ^ ProgressThreshold;
            }
        }

        public static bool operator ==(FeatureGateState left, FeatureGateState right) => left.Equals(right);
        public static bool operator !=(FeatureGateState left, FeatureGateState right) => !left.Equals(right);
    }
}
