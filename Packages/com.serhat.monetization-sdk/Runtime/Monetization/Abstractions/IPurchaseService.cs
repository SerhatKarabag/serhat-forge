#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Monetization.Domain;

namespace Serhat.Backend.Monetization.Abstractions
{
    /// <summary>
    /// Main purchase service interface.
    /// Orchestrates store interactions, server verification, and entitlement management.
    /// </summary>
    public interface IPurchaseService
    {
        /// <summary>
        /// Whether the store is initialized and ready for purchases.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Current active subscription if any.
        /// </summary>
        SubscriptionDto? ActiveSubscription { get; }

        /// <summary>
        /// Event raised when initialization completes.
        /// </summary>
        event Action<InitializationResult>? OnInitialized;

        /// <summary>
        /// Event raised when a purchase completes (success or failure).
        /// </summary>
        event Action<PurchaseResult>? OnPurchaseCompleted;

        /// <summary>
        /// Event raised when entitlements are updated.
        /// </summary>
        event Action<EntitlementsResponse>? OnEntitlementsUpdated;

        /// <summary>
        /// Initializes the store and fetches product catalog.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Initialization result with available products.</returns>
        Task<InitializationResult> InitializeAsync(CancellationToken ct = default);

        /// <summary>
        /// Initiates a purchase for the specified product.
        /// </summary>
        /// <param name="productId">Store product ID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Purchase result.</returns>
        Task<PurchaseResult> BuyAsync(string productId, CancellationToken ct = default);

        /// <summary>
        /// Restores previously purchased non-consumables and subscriptions.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Restore result with all restored purchases.</returns>
        Task<RestoreResult> RestoreAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets current entitlements from the server.
        /// </summary>
        /// <param name="forceRefresh">Force server refresh, bypassing cache.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Current entitlements.</returns>
        Task<EntitlementsResponse> GetEntitlementsAsync(
            bool forceRefresh = false,
            CancellationToken ct = default);

        /// <summary>
        /// Checks if the player owns a specific entitlement.
        /// </summary>
        /// <param name="itemId">Economy item ID.</param>
        /// <returns>True if owned.</returns>
        bool HasEntitlement(string itemId);

        /// <summary>
        /// Gets information about a specific product.
        /// </summary>
        /// <param name="productId">Store product ID.</param>
        /// <returns>Product info or null if not found.</returns>
        ProductInfo? GetProductInfo(string productId);

        /// <summary>
        /// Processes any pending purchases that weren't verified due to crash/network issues.
        /// Called automatically during initialization but can be called manually.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        Task ProcessPendingPurchasesAsync(CancellationToken ct = default);
    }
}
