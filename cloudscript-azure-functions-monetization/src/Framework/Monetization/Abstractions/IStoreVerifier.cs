using System.Threading;
using System.Threading.Tasks;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;

/// <summary>
/// Request for purchase verification.
/// </summary>
public sealed class VerifyRequest
{
    /// <summary>
    /// Store product ID.
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Transaction ID from the store.
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Receipt payload.
    /// - iOS: Base64 encoded App Store receipt
    /// - Android: Purchase token
    /// </summary>
    public string ReceiptPayload { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a subscription.
    /// </summary>
    public bool IsSubscription { get; set; }

    /// <summary>
    /// Android: Package name.
    /// </summary>
    public string? PackageName { get; set; }
}

/// <summary>
/// Abstraction for store-specific verification.
/// </summary>
public interface IStoreVerifier
{
    /// <summary>
    /// Platform identifier (apple/google).
    /// </summary>
    string Platform { get; }

    /// <summary>
    /// Verifies a one-time purchase (consumable or non-consumable).
    /// </summary>
    Task<VerificationResult> VerifyOneTimePurchaseAsync(
        VerifyRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies a subscription purchase.
    /// </summary>
    Task<VerificationResult> VerifySubscriptionAsync(
        VerifyRequest request,
        CancellationToken ct = default);
}
