namespace Serhat.Forge.FeatureGates
{
    /// <summary>
    /// Stable feature identifiers. Numeric values are persistence keys and must never
    /// be reused. Values may be sparse; runtime code does not depend on enum ordering.
    /// </summary>
    public enum FeatureId
    {
        None = 0,
        // Add project-specific IDs with explicit stable numeric values.
        // Shop = 10,
        // DailyReward = 20,
    }
}
