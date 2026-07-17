#nullable enable

using System.Collections.Generic;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Domain;

namespace Serhat.Backend.Monetization.Samples
{
    /// <summary>
    /// Example implementation of IProductCatalogMapping.
    /// Each game should implement this interface with their own product definitions.
    /// </summary>
    /// <remarks>
    /// This is a sample - do not use in production without replacing
    /// with your actual product IDs and configuration.
    /// </remarks>
    public sealed class ExampleProductCatalog : IProductCatalogMapping
    {
        // Example product IDs - replace with your actual store product IDs
        public const string CoinsPack100 = "com.example.game.coins_100";
        public const string CoinsPack500 = "com.example.game.coins_500";
        public const string CoinsPack1000 = "com.example.game.coins_1000";
        public const string RemoveAds = "com.example.game.remove_ads";
        public const string PremiumMonthly = "com.example.game.premium_monthly";
        public const string ProMonthly = "com.example.game.pro_monthly";

        // Tier keys
        public const string TierPremium = "premium";
        public const string TierPro = "pro";

        private readonly Dictionary<string, ProductDefinition> _products;

        public ExampleProductCatalog()
        {
            _products = new Dictionary<string, ProductDefinition>
            {
                // Consumables
                [CoinsPack100] = new ProductDefinition(
                    productId: CoinsPack100,
                    type: ProductType.Consumable,
                    metadata: "coins:100"),

                [CoinsPack500] = new ProductDefinition(
                    productId: CoinsPack500,
                    type: ProductType.Consumable,
                    metadata: "coins:500"),

                [CoinsPack1000] = new ProductDefinition(
                    productId: CoinsPack1000,
                    type: ProductType.Consumable,
                    metadata: "coins:1000"),

                // Non-consumable
                [RemoveAds] = new ProductDefinition(
                    productId: RemoveAds,
                    type: ProductType.NonConsumable),

                // Subscriptions with tiers
                [PremiumMonthly] = new ProductDefinition(
                    productId: PremiumMonthly,
                    type: ProductType.Subscription,
                    tierKey: TierPremium,
                    tierPrecedence: 1),

                [ProMonthly] = new ProductDefinition(
                    productId: ProMonthly,
                    type: ProductType.Subscription,
                    tierKey: TierPro,
                    tierPrecedence: 2)  // Higher precedence = better tier
            };
        }

        public IReadOnlyList<ProductDefinition> GetAllProducts()
        {
            return new List<ProductDefinition>(_products.Values);
        }

        public ProductDefinition? GetProduct(string productId)
        {
            _products.TryGetValue(productId, out var product);
            return product;
        }

        public IReadOnlyList<ProductDefinition> GetSubscriptionProducts()
        {
            var subscriptions = new List<ProductDefinition>();
            foreach (var product in _products.Values)
            {
                if (product.IsSubscription)
                {
                    subscriptions.Add(product);
                }
            }
            return subscriptions;
        }

        public IReadOnlyList<ProductDefinition> GetProductsByTier(string tierKey)
        {
            var products = new List<ProductDefinition>();
            foreach (var product in _products.Values)
            {
                if (product.TierKey == tierKey)
                {
                    products.Add(product);
                }
            }
            return products;
        }

        public bool IsProductAllowed(string productId)
        {
            return _products.ContainsKey(productId);
        }
    }
}
