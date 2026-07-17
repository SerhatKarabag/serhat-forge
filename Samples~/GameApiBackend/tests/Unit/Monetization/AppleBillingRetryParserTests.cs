using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Verification;
using Serhat.Forge.CloudScript.Framework.Monetization.Webhooks;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public sealed class AppleBillingRetryParserTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("BILLING_RETRY", 3, 60, WebhookEventType.GracePeriodEnded)]
    [InlineData("GRACE_PERIOD", 3, 60, WebhookEventType.GracePeriodEnded)]
    [InlineData("GRACE_PERIOD", 4, -1, WebhookEventType.GracePeriodEnded)]
    [InlineData("GRACE_PERIOD", 4, 60, WebhookEventType.GracePeriodStarted)]
    public void DidFailToRenew_RetainsBenefitsOnlyForAuthoritativeFutureGracePeriod(
        string subtype,
        int status,
        int graceMinutes,
        WebhookEventType expectedEventType)
    {
        var parser = CreateParser(
            "DID_FAIL_TO_RENEW",
            subtype,
            status,
            Now.AddMinutes(graceMinutes));

        var result = parser.Parse("{\"signedPayload\":\"outer\"}");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Event);
        Assert.Equal(expectedEventType, result.Event.EventType);
    }

    [Fact]
    public void DidRecover_MapsToRecoveredLifecycleEvent()
    {
        var parser = CreateParser("DID_RECOVER", null, 1, null);

        var result = parser.Parse("{\"signedPayload\":\"outer\"}");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Event);
        Assert.Equal(WebhookEventType.Recovered, result.Event.EventType);
    }

    private static AppleNotificationParser CreateParser(
        string notificationType,
        string? subtype,
        int status,
        DateTimeOffset? gracePeriodEnd)
    {
        const string bundleId = "com.test.app";
        const long appAppleId = 123456789;
        var signedDate = Now.ToUnixTimeMilliseconds();

        var payloads = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["outer"] = JsonSerializer.Serialize(new
            {
                notificationType,
                subtype,
                notificationUUID = $"event-{notificationType}-{subtype}-{status}-{gracePeriodEnd}",
                signedDate,
                data = new
                {
                    appAppleId,
                    bundleId,
                    environment = "Sandbox",
                    signedTransactionInfo = "transaction",
                    signedRenewalInfo = "renewal",
                    status
                }
            }),
            ["transaction"] = JsonSerializer.Serialize(new
            {
                transactionId = "transaction-1",
                originalTransactionId = "original-1",
                bundleId,
                productId = "com.test.app.subscription",
                environment = "Sandbox",
                purchaseDate = Now.AddDays(-1).ToUnixTimeMilliseconds(),
                expiresDate = Now.AddDays(1).ToUnixTimeMilliseconds()
            }),
            ["renewal"] = JsonSerializer.Serialize(new
            {
                originalTransactionId = "original-1",
                productId = "com.test.app.subscription",
                environment = "Sandbox",
                autoRenewStatus = 1,
                gracePeriodExpiresDate = gracePeriodEnd?.ToUnixTimeMilliseconds()
            })
        };

        return new AppleNotificationParser(
            new AppleNotificationConfig
            {
                BundleId = bundleId,
                AppAppleId = appAppleId,
                ExpectedEnvironment = "Sandbox",
                HostEnvironmentName = "Test",
                MaxNotificationAge = TimeSpan.FromDays(1)
            },
            new StubAppleJwsVerifier(payloads),
            NullLogger<AppleNotificationParser>.Instance,
            new FixedTimeProvider(Now));
    }

    private sealed class StubAppleJwsVerifier : IAppleJwsVerifier
    {
        private readonly IReadOnlyDictionary<string, string> _payloads;

        public StubAppleJwsVerifier(IReadOnlyDictionary<string, string> payloads)
        {
            _payloads = payloads;
        }

        public AppleJwsVerificationResult Verify(string compactJws) =>
            _payloads.TryGetValue(compactJws, out var payload)
                ? AppleJwsVerificationResult.Success(payload)
                : AppleJwsVerificationResult.Failure("UNKNOWN_TEST_JWS");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
