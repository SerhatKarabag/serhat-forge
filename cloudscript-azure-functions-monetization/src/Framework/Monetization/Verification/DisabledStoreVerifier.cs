using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Verification;

/// <summary>
/// Fail-closed verifier used when a deployment intentionally disables one store.
/// </summary>
public sealed class DisabledStoreVerifier : IStoreVerifier
{
    public DisabledStoreVerifier(string platform)
    {
        Platform = platform;
    }

    public string Platform { get; }

    public Task<VerificationResult> VerifyOneTimePurchaseAsync(
        VerifyRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(Disabled());

    public Task<VerificationResult> VerifySubscriptionAsync(
        VerifyRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(Disabled());

    private VerificationResult Disabled() =>
        VerificationResult.Invalid(
            "STORE_DISABLED",
            $"{Platform} purchase verification is disabled for this deployment");
}
