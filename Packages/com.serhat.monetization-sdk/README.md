# Serhat Monetization SDK

Optional Unity IAP client orchestration for store products, server verification, crash-recoverable pending purchases, entitlements, restores, and subscription tier policies.

This package is not a complete payment system by itself. A shipped game must use the hardened monetization Function App (or an equivalent server) to verify every receipt and grant every entitlement.

## Purchase boundary

```text
Unity IAP -> pending receipt -> trusted backend verification
          -> idempotent server grant -> confirm store purchase
          -> refresh server entitlements
```

The client never treats a store callback as an entitlement grant. `PurchaseService` confirms a pending store purchase only after `IMonetizationBackendClient` returns successful verification.

## Requirements

- Unity 6000.3 or newer
- Unity IAP 5.2.0 (`com.unity.purchasing`)
- Serhat Backend SDK (`com.serhat.backend-sdk`)
- A configured `ICloudFunctionInvoker`
- A deployed and secured monetization backend
- Matching product IDs in Unity, Apple App Store Connect/Google Play Console, and the server allowlist

The package declares Unity Purchasing `5.2.0` as a required dependency. Its provider-independent core and `Serhat.BackendSdk.Monetization.UnityIap` adapter compile automatically, with no scripting-define gate. Installing the package does **not** initialize a store or enable purchases: runtime activation happens only when the game composition root supplies a catalog, creates `UnityIapStoreClient`, connects a verified backend, and owns the resulting `PurchaseService` lifecycle.

Serhat Forge embeds this package and `com.serhat.backend-sdk` together. When evaluating only
the monetization package from a Git URL, add both package subpaths to the consuming project's
`Packages/manifest.json` at the same immutable tag or commit; Unity Package Manager cannot
resolve the sibling Serhat package from this repository by its preview SemVer alone:

```json
{
  "dependencies": {
    "com.serhat.backend-sdk": "https://github.com/SerhatKarabag/serhat-forge.git?path=Packages/com.serhat.backend-sdk#<tag-or-commit>",
    "com.serhat.monetization-sdk": "https://github.com/SerhatKarabag/serhat-forge.git?path=Packages/com.serhat.monetization-sdk#<same-tag-or-commit>"
  }
}
```

## Production wiring

Create the service in your composition root and retain the same instance for the application lifetime. The example below uses only APIs provided by the packages:

```csharp
using Serhat.Backend.Core;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Backend;
using Serhat.Backend.Monetization.Domain;
using Serhat.Backend.Monetization.Persistence;
using Serhat.Backend.Monetization.Services;
using Serhat.Backend.Monetization.Store;

public static class MonetizationComposition
{
    public static IPurchaseService Create(
        ICloudFunctionInvoker monetizationInvoker,
        string authenticatedPlayerId,
        IProductCatalogMapping catalog,
        ITierPolicy tierPolicy,
        IStorage protectedPendingPurchaseStorage)
    {
        var clock = SystemClock.Instance;
        var backend = new MonetizationBackendClientBuilder()
            .WithInvoker(monetizationInvoker)
            .WithClock(clock)
            .Build();

        var pendingStore = new PendingPurchaseStore(
            protectedPendingPurchaseStorage,
            clock);

        var store = new UnityIapStoreClient();
        store.SetGoogleObfuscatedAccountId(
            StoreAccountIdentity.CreateGoogleObfuscatedAccountId(authenticatedPlayerId));
        store.SetAppleAppAccountToken(
            StoreAccountIdentity.CreateAppleAppAccountToken(authenticatedPlayerId));

        return new PurchaseService(
            store,
            backend,
            catalog,
            tierPolicy,
            pendingStore,
            clock,
            new UnityBackendLogger("Monetization"));
    }
}
```

Pass the stable, authenticated PlayFab title-player ID used by the backend—not a display
name, device ID, or client-generated guest value. Configure both store account bindings
before `InitializeAsync`; the backend derives the same non-PII Google hash and Apple
deterministic UUIDv8 from its trusted PlayFab wrapper and rejects purchases assigned to
another account. Recreate the
application-lifetime store service after an authenticated-account change.

The pending-purchase payload contains Google purchase tokens and Apple transaction IDs. Apple AppReceipt/JWS data is deliberately neither extracted, persisted, nor sent because App Store Server API verification queries by transaction ID. Supply a thread-safe `IStorage` implementation backed by platform-protected or encrypted storage whose read/write operations may run on a worker thread. The pending store waits for durable writes before backend verification continues. The SDK's `FileStorage` is convenient for local development but writes plaintext JSON; do not use it when the production threat model includes device compromise, and never sync this data through analytics, crash reports, or cloud backup.

`PurchaseService` implements `IDisposable` and owns the store adapter's event subscriptions. Bind it as a Zenject singleton so the container disposes it, or dispose it explicitly when a manually constructed application lifetime ends. Do not create multiple live `UnityIapStoreClient` instances over Unity IAP's shared store services.

`monetizationInvoker` must target the separate hardened Function App and its registered PlayFab functions:

- `VerifyPurchase`
- `GetEntitlements`

Do not route this client to `IapVerify`, `IapGetEntitlements`, or `GrantPurchaseRewards` in `Samples~/GameApiBackend`; those migration-only endpoints always return `410 Gone / LEGACY_MONETIZATION_DISABLED`.

## Product catalog

`ProductDefinition` is immutable and constructor-based. Implement every `IProductCatalogMapping` member:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Domain;

public sealed class GameProductCatalog : IProductCatalogMapping
{
    public const string Coins100 = "com.company.game.coins_100";
    public const string PremiumMonthly = "com.company.game.premium_monthly";

    private static readonly ProductDefinition[] Products =
    {
        new(Coins100, ProductType.Consumable, metadata: "coins:100"),
        new(
            PremiumMonthly,
            ProductType.Subscription,
            tierKey: "premium",
            tierPrecedence: 1)
    };

    public IReadOnlyList<ProductDefinition> GetAllProducts() => Products;

    public ProductDefinition? GetProduct(string productId) =>
        Products.FirstOrDefault(product =>
            string.Equals(product.ProductId, productId, StringComparison.Ordinal));

    public IReadOnlyList<ProductDefinition> GetSubscriptionProducts() =>
        Products.Where(product => product.IsSubscription).ToArray();

    public IReadOnlyList<ProductDefinition> GetProductsByTier(string tierKey) =>
        Products.Where(product =>
            string.Equals(product.TierKey, tierKey, StringComparison.Ordinal)).ToArray();

    public bool IsProductAllowed(string productId) => GetProduct(productId) != null;
}
```

Implement `ITierPolicy` for subscription precedence and validation. The Package Manager sample includes complete catalog and tier-policy examples.

Serhat Forge deliberately does not perform subscription replacement automatically. When the backend reports an active subscription, buying the same SKU returns `AlreadyOwned`; buying a different subscription SKU returns `SubscriptionChangeNotSupported`. Google replacement requires an explicit billing replacement mode, linked-purchase-token handling, RTDN lifecycle support, and sandbox/device validation. Implement that project-specific end-to-end flow before exposing upgrade or downgrade UI—never fall back to a normal `PurchaseProduct` call.

## Initialize and purchase

Await initialization and inspect its result before enabling store UI:

```csharp
var initialization = await purchaseService.InitializeAsync(cancellationToken);
if (!initialization.IsSuccess)
{
    Debug.LogWarning($"Store unavailable: {initialization.Error}");
    return;
}

var product = purchaseService.GetProductInfo(GameProductCatalog.Coins100);
priceLabel.text = product?.PriceString ?? "Unavailable";
buyButton.interactable = product != null;
```

Disable duplicate input while awaiting `BuyAsync`, then update UI only from server-confirmed state:

```csharp
buyButton.interactable = false;
try
{
    var result = await purchaseService.BuyAsync(
        GameProductCatalog.Coins100,
        cancellationToken);

    if (!result.IsSuccess)
    {
        Debug.LogWarning($"Purchase failed: {result.Error}");
        return;
    }

    Debug.Log($"Verified purchase: {result.ProductId}");
    Debug.Log($"Server grants: {string.Join(", ", result.GrantedItemIds)}");

    // Reconcile inventory/currency from your authoritative backend state here.
}
finally
{
    buyButton.interactable = true;
}
```

Do not add currency or unlock content merely because Unity IAP reported success. The trusted outcome is the successful backend response and subsequent entitlement/inventory reconciliation.

## Entitlements and restore

```csharp
var entitlements = await purchaseService.GetEntitlementsAsync(
    forceRefresh: true,
    ct: cancellationToken);

bool ownsRemoveAds = purchaseService.HasEntitlement("remove_ads_item");
var activeSubscription = entitlements.ActiveSubscription;
```

Each entitlement represents one PlayFab Economy inventory stack and includes `ItemId`,
`StackId`, the authoritative 64-bit `Quantity`, and optional `ExpiresAtUtc`. Do not assume every
stack has quantity one. Backend inventory failures are errors, not successful empty responses;
keep the last known UI state non-authoritative and offer retry instead of removing owned content.

Use `RestoreAsync` for non-consumables and subscriptions. Inspect `Status`, `RestoredPurchases`, and `FailedPurchases`; `IsPartialSuccess` means at least one receipt restored and at least one failed. A restore where every discovered receipt fails is a failed operation, not an empty success. Never restore consumable balance from local receipts.

## Failure and lifecycle handling

- Keep one purchase operation in flight. `PurchaseService` rejects concurrent purchases.
- Purchase and restore operations share the same operation gate; treat a `Pending` result as a request to wait for the active operation to finish.
- Pass a lifetime cancellation token to UI-triggered calls and stop accepting input during teardown.
- Cancellation ends the local wait; it cannot revoke a native store transaction already accepted by Apple or Google. Late callbacks are quarantined and reconciled through the pending-purchase path.
- Successful backend verification is not enough to delete recovery state. The SDK awaits Unity IAP's confirmation callback and retains the durable pending receipt on confirmation failure or timeout.
- Initialization starts pending-purchase processing and entitlement refresh. Explicitly await `GetEntitlementsAsync(true)` before opening entitlement-dependent UI.
- Every subscription buy performs a bounded fresh entitlement query and fails closed when that query is unavailable; a stale local cache can never authorize a second subscription.
- Pending receipts survive restarts and remain in a lifetime recovery loop with persisted exponential backoff capped at 30 minutes; retryable purchases are not discarded after a fixed attempt count. Do not clear their storage during ordinary logout/update flows.
- Subscribe to service events only from a long-lived owner and unsubscribe when replacing the service instance.
- Network loss, cancellation, or app termination can leave a transaction pending; test restart recovery on real devices.

## Security and release checklist

- [ ] Store product IDs exactly match the client catalog and server `ALLOWED_PRODUCTS_JSON`.
- [ ] `VerifyPurchase` and `GetEntitlements` are registered against the hardened monetization Function App.
- [ ] PlayFab caller identity is bound server-side; no client-supplied player ID is trusted in production.
- [ ] Google purchases set the obfuscated account ID before store initialization, and backend ownership validation is enabled.
- [ ] Apple purchases set the deterministic `appAccountToken` before store initialization, and backend ownership validation is enabled.
- [ ] Apple JWS/certificate validation and Google purchase/RTDN validation are enabled.
- [ ] `USE_FAKE_VERIFIER=false` and signature bypasses are disabled outside local/test environments.
- [ ] Consumables, non-consumables, subscriptions, restores, refunds, renewals, grace periods, and revoked purchases are tested in store sandboxes.
- [ ] PlayFab Economy v2 inventory pagination, large quantities, multiple stacks, expiry, and provider outages are tested with the game's catalog.
- [ ] No store secrets, PlayFab developer secrets, private keys, receipts, or access tokens are logged or committed.
- [ ] Pending-token storage matches the game's device-compromise and backup threat model; Apple AppReceipt/JWS data never enters it, while the default `FileStorage` remains plaintext for Google tokens.

See `cloudscript-azure-functions-monetization/README.md` for backend deployment and security configuration.

## Sample

Import **Monetization Example** from Package Manager and read its included `README.md`. Its mock backend is development-only and deliberately fails closed in release players.
