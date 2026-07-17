using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Verification;

/// <summary>
/// Fake store verifier for testing and development.
/// Always returns valid results for any receipt.
/// </summary>
public sealed class FakeStoreVerifier : IStoreVerifier
{
    public string Platform => "fake";

    private readonly bool _alwaysSucceed;
    private volatile bool _failureMode;
    private string _failureCode = "INVALID_RECEIPT";
    private string _failureMessage = "Receipt validation failed";
    private readonly TimeSpan _delay;

    public FakeStoreVerifier(bool alwaysSucceed = true, TimeSpan? delay = null)
    {
        _alwaysSucceed = alwaysSucceed;
        _delay = delay ?? TimeSpan.Zero;
    }

    public void SetFailureMode(bool enabled, string? errorCode, string? errorMessage)
    {
        _failureCode = string.IsNullOrWhiteSpace(errorCode) ? "INVALID_RECEIPT" : errorCode;
        _failureMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? "Receipt validation failed"
            : errorMessage;
        _failureMode = enabled;
    }
    public async Task<VerificationResult> VerifyOneTimePurchaseAsync(
        VerifyRequest request,
        CancellationToken ct = default)
    {
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, ct);
        }

        if (!_alwaysSucceed || _failureMode)
        {
            return VerificationResult.Invalid(_failureCode, _failureMessage);
        }

        return VerificationResult.Valid() with
        {
            ProductId = request.ProductId,
            TransactionId = request.TransactionId,
            PurchaseDateUtc = DateTime.UtcNow,
            IsSubscription = false,
            IsSandbox = true
        };
    }

    public async Task<VerificationResult> VerifySubscriptionAsync(
        VerifyRequest request,
        CancellationToken ct = default)
    {
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, ct);
        }

        if (!_alwaysSucceed || _failureMode)
        {
            return VerificationResult.Invalid(_failureCode, _failureMessage);
        }

        return VerificationResult.Valid() with
        {
            ProductId = request.ProductId,
            TransactionId = request.TransactionId,
            OriginalTransactionId = $"orig_{request.TransactionId}",
            PurchaseDateUtc = DateTime.UtcNow,
            ExpirationDateUtc = DateTime.UtcNow.AddMonths(1),
            IsSubscription = true,
            SubscriptionStatus = Domain.SubscriptionStatus.Active,
            AutoRenew = true,
            IsSandbox = true
        };
    }
}
