#nullable enable

using System.Collections.Generic;
using Serhat.Backend.Monetization.Domain;

namespace Serhat.Backend.Monetization.Abstractions
{
    /// <summary>
    /// Game-specific product catalog mapping.
    /// Implement this interface to define your game's products.
    /// </summary>
    /// <remarks>
    /// This is the primary extension point for games to define their product catalog
    /// without modifying the reusable monetization code.
    /// </remarks>
    public interface IProductCatalogMapping
    {
        /// <summary>
        /// Gets all product definitions for this game.
        /// </summary>
        /// <returns>Collection of product definitions to register with the store.</returns>
        IReadOnlyList<ProductDefinition> GetAllProducts();

        /// <summary>
        /// Gets a product definition by store product ID.
        /// </summary>
        /// <param name="productId">Store product ID.</param>
        /// <returns>Product definition or null if not found.</returns>
        ProductDefinition? GetProduct(string productId);

        /// <summary>
        /// Gets all subscription products.
        /// </summary>
        IReadOnlyList<ProductDefinition> GetSubscriptionProducts();

        /// <summary>
        /// Gets all products for a specific tier.
        /// </summary>
        /// <param name="tierKey">Tier key.</param>
        IReadOnlyList<ProductDefinition> GetProductsByTier(string tierKey);

        /// <summary>
        /// Validates if a product ID is allowed for purchase.
        /// </summary>
        /// <param name="productId">Store product ID.</param>
        /// <returns>True if allowed.</returns>
        bool IsProductAllowed(string productId);
    }
}
