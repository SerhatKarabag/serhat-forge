#nullable enable

using System;

namespace Serhat.Backend.Monetization.Domain
{
    /// <summary>
    /// Type of product in the store.
    /// </summary>
    public enum ProductType
    {
        /// <summary>Can be purchased multiple times (coins, gems, etc.).</summary>
        Consumable,

        /// <summary>One-time purchase that persists (remove ads, unlock level pack).</summary>
        NonConsumable,

        /// <summary>Recurring subscription.</summary>
        Subscription
    }

    /// <summary>
    /// Product definition for catalog mapping.
    /// Game provides these via IProductCatalogMapping.
    /// </summary>
    public sealed class ProductDefinition
    {
        /// <summary>Store product ID (e.g., "com.example.game.premium_monthly").</summary>
        public string ProductId { get; }

        /// <summary>Type of product.</summary>
        public ProductType Type { get; }

        /// <summary>For subscriptions: tier key for validation and project-specific replacement logic.</summary>
        public string? TierKey { get; }

        /// <summary>For subscriptions: tier precedence (higher = better tier).</summary>
        public int TierPrecedence { get; }

        /// <summary>Optional metadata for game-specific use.</summary>
        public string? Metadata { get; }

        public ProductDefinition(
            string productId,
            ProductType type,
            string? tierKey = null,
            int tierPrecedence = 0,
            string? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                throw new ArgumentException("Product ID is required.", nameof(productId));
            }

            ProductId = productId;
            Type = type;
            TierKey = tierKey;
            TierPrecedence = tierPrecedence;
            Metadata = metadata;
        }

        public bool IsSubscription => Type == ProductType.Subscription;
        public bool IsConsumable => Type == ProductType.Consumable;
        public bool IsNonConsumable => Type == ProductType.NonConsumable;
    }
}
