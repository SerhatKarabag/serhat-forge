using System.Collections.Generic;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Domain;

/// <summary>
/// Product configuration from ALLOWED_PRODUCTS_JSON.
/// </summary>
public sealed class ProductConfig
{
    /// <summary>
    /// Store product ID.
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Product type.
    /// </summary>
    public ProductType Type { get; set; }

    /// <summary>
    /// Economy item ID(s) to grant.
    /// </summary>
    public List<string> EconomyItemIds { get; set; } = new();

    /// <summary>
    /// Quantity to grant (for consumables).
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// For subscriptions: tier key.
    /// </summary>
    public string? TierKey { get; set; }

    /// <summary>
    /// For subscriptions: tier precedence (higher = better).
    /// </summary>
    public int TierPrecedence { get; set; }

    /// <summary>
    /// Whether this product is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public bool IsSubscription => Type == ProductType.Subscription;
}

/// <summary>
/// Product allowlist configuration.
/// Loaded from ALLOWED_PRODUCTS_JSON environment variable.
/// </summary>
public sealed class ProductAllowlistConfig
{
    /// <summary>
    /// Map of product ID to configuration.
    /// </summary>
    public Dictionary<string, ProductConfig> Products { get; set; } = new();

    /// <summary>
    /// Whether to allow sandbox purchases in production.
    /// </summary>
    public bool AllowSandboxInProduction { get; set; } = false;

    /// <summary>
    /// Gets a product configuration by ID.
    /// </summary>
    public ProductConfig? GetProduct(string productId)
    {
        Products.TryGetValue(productId, out var config);
        return config?.Enabled == true ? config : null;
    }

    /// <summary>
    /// Checks if a product is allowed.
    /// </summary>
    public bool IsProductAllowed(string productId)
    {
        return GetProduct(productId) != null;
    }
}
