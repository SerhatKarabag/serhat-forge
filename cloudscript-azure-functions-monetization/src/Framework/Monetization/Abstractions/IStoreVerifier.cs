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
    /// Platform verification payload. Google requires the purchase token. Apple verification is
    /// transaction-ID based and this value must remain empty so raw App Store receipts are not
    /// transported or persisted.
    /// </summary>
    public string ReceiptPayload { get; set; } = string.Empty;

    /// <summary>
    /// Stable, non-PII player binding sent to Google Play as the obfuscated account ID.
    /// Required by production Google verification when account binding is enabled.
    /// </summary>
    public string? ExpectedObfuscatedAccountId { get; set; }

    /// <summary>
    /// Stable UUID supplied to StoreKit as appAccountToken for the authenticated player.
    /// Required by production Apple verification when account binding is enabled.
    /// </summary>
    public string? ExpectedAppleAppAccountToken { get; set; }

    /// <summary>
    /// Server-authoritative product type from the immutable allowlist snapshot. Apple signed
    /// transaction type must match this value exactly.
    /// </summary>
    public ProductType? ExpectedProductType { get; set; }

    /// <summary>
    /// Whether this is a subscription.
    /// </summary>
    public bool IsSubscription { get; set; }
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
