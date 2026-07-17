# Serhat Monetization SDK

Unity IAP and subscription layer with backend verification, tier policies, and PlayFab-backed entitlements.

## Installation

This package is already embedded in Serhat Forge under `Packages/com.serhat.monetization-sdk`.

If you extract it for another project, copy the entire package directory and retain its `LICENSE.md`. The package also requires the Serhat Backend SDK and Unity IAP versions declared in `package.json`.

## Quick Start

```csharp
using Serhat.Backend.Monetization.Services;
using Serhat.Backend.Monetization.Abstractions;
using UnityEngine;

public class StoreManager : MonoBehaviour
{
    private IPurchaseService _purchaseService;

    async void Start()
    {
        _purchaseService = CreatePurchaseService();
        await _purchaseService.InitializeAsync();
    }

    public async void BuyCoins()
    {
        var result = await _purchaseService.BuyAsync("coins_100");
        if (result.IsSuccess)
        {
            Debug.Log($"Purchased! Granted: {string.Join(", ", result.GrantedItemIds)}");
        }
    }
}
```

## Implementing Your Product Catalog

```csharp
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Domain;

public class MyProductCatalog : IProductCatalogMapping
{
    private readonly Dictionary<string, ProductDefinition> _products = new()
    {
        ["coins_100"] = new ProductDefinition
        {
            ProductId = "coins_100",
            Type = ProductType.Consumable,
            DisplayName = "100 Coins"
        },
        ["premium_monthly"] = new ProductDefinition
        {
            ProductId = "premium_monthly",
            Type = ProductType.Subscription,
            TierKey = "premium",
            TierPrecedence = 1
        }
    };

    public IReadOnlyList<ProductDefinition> GetAllProducts()
        => _products.Values.ToList();

    public ProductDefinition? GetProduct(string productId)
        => _products.GetValueOrDefault(productId);
}
```

## Requirements

- Unity 6000.3 or newer
- Unity IAP 5.2.0 (`com.unity.purchasing`)
- Serhat Backend SDK (`com.serhat.backend-sdk`)
