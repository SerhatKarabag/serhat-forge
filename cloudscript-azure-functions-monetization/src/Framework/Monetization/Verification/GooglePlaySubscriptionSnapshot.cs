using System;
using System.Threading;
using System.Threading.Tasks;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Verification;

/// <summary>
/// Reads the authoritative Google Play subscription state for a purchase token.
/// RTDN handlers can use this contract to re-query Google instead of trusting the
/// notification payload as subscription state.
/// </summary>
public interface IGooglePlaySubscriptionSnapshotProvider
{
    Task<GooglePlaySubscriptionQueryResult> QuerySubscriptionAsync(
        string purchaseToken,
        CancellationToken ct = default);
}

public enum GooglePlaySubscriptionState
{
    Unspecified,
    Pending,
    Active,
    Paused,
    InGracePeriod,
    OnHold,
    Canceled,
    Expired,
    PendingPurchaseCanceled
}

public enum GooglePlaySubscriptionQueryFailure
{
    None,
    Permanent,
    Retryable
}

/// <summary>
/// A normalized, single-line-item view of a subscriptionsv2.get response.
/// Purchase-token fields are sensitive and must never be logged.
/// </summary>
public sealed record GooglePlaySubscriptionSnapshot
{
    public required GooglePlaySubscriptionState State { get; init; }
    public required string ProductId { get; init; }
    public DateTime? StartTimeUtc { get; init; }
    public DateTime? ExpiryTimeUtc { get; init; }
    public string? LatestSuccessfulOrderId { get; init; }
    public bool AutoRenewEnabled { get; init; }
    public bool IsTestPurchase { get; init; }
    public string? LinkedPurchaseToken { get; init; }
    public GooglePlayExternalAccountIdentifiers? ExternalAccountIdentifiers { get; init; }
}

public sealed record GooglePlayExternalAccountIdentifiers
{
    public string? ExternalAccountId { get; init; }
    public string? ObfuscatedExternalAccountId { get; init; }
    public string? ObfuscatedExternalProfileId { get; init; }
}

public sealed record GooglePlaySubscriptionQueryResult
{
    private GooglePlaySubscriptionQueryResult(
        GooglePlaySubscriptionSnapshot? snapshot,
        GooglePlaySubscriptionQueryFailure failure,
        string? errorCode,
        string? errorMessage)
    {
        Snapshot = snapshot;
        Failure = failure;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public GooglePlaySubscriptionSnapshot? Snapshot { get; }
    public GooglePlaySubscriptionQueryFailure Failure { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public bool IsSuccess => Snapshot != null && Failure == GooglePlaySubscriptionQueryFailure.None;

    public static GooglePlaySubscriptionQueryResult Success(
        GooglePlaySubscriptionSnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)),
            GooglePlaySubscriptionQueryFailure.None,
            null,
            null);

    public static GooglePlaySubscriptionQueryResult Permanent(
        string errorCode,
        string errorMessage) =>
        new(null, GooglePlaySubscriptionQueryFailure.Permanent, errorCode, errorMessage);

    public static GooglePlaySubscriptionQueryResult Retryable(
        string errorCode,
        string errorMessage) =>
        new(null, GooglePlaySubscriptionQueryFailure.Retryable, errorCode, errorMessage);
}
