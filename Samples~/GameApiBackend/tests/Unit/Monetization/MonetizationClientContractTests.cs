extern alias MonetizationCloud;

using System.Text.Json;
using System.Text.Json.Serialization;
using Serhat.Backend.Core;
using Serhat.Backend.Monetization.Backend;
using Serhat.Backend.Monetization.Domain;
using Serhat.Forge.CloudScript.Domain.DTOs;
using Serhat.Forge.CloudScript.Functions.Monetization;
using Xunit;
using ClientGetEntitlementsResponse = Serhat.Backend.Monetization.Backend.GetEntitlementsResponse;
using ClientVerifyPurchaseResponse = Serhat.Backend.Monetization.Backend.VerifyPurchaseResponse;
using ServerGetEntitlementsResponse = Serhat.Forge.CloudScript.Functions.Monetization.GetEntitlementsResponseDto;
using ServerVerifyPurchaseResponse = Serhat.Forge.CloudScript.Functions.Monetization.VerifyPurchaseResponseDto;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public sealed class MonetizationClientContractTests
{
    private static readonly JsonSerializerOptions ContractJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task VerifyPurchaseAsync_InvokesHardenedFunction_WithIdempotency()
    {
        var invoker = new CapturingInvoker(new ClientVerifyPurchaseResponse { Success = true });
        var client = new MonetizationBackendClient(invoker, new FixedClock());
        var request = new VerifyPurchaseRequest
        {
            Platform = "google",
            ProductId = "coins_100",
            TransactionId = "transaction-1",
            ReceiptPayload = "receipt-token"
        };

        var result = await client.VerifyPurchaseAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("VerifyPurchase", invoker.FunctionName);
        Assert.Same(request, invoker.Request);
        Assert.NotNull(invoker.Options);
        Assert.NotNull(invoker.Options.IdempotencyKey);
        Assert.False(string.IsNullOrWhiteSpace(invoker.Options.CorrelationId));
    }

    [Fact]
    public async Task GetEntitlementsAsync_InvokesHardenedFunction()
    {
        var invoker = new CapturingInvoker(new ClientGetEntitlementsResponse());
        var client = new MonetizationBackendClient(invoker, new FixedClock());
        var request = new GetEntitlementsRequest { ForceRefresh = true };

        var result = await client.GetEntitlementsAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("GetEntitlements", invoker.FunctionName);
        Assert.Same(request, invoker.Request);
        Assert.NotNull(invoker.Options);
        Assert.Null(invoker.Options.IdempotencyKey);
        Assert.Equal("entitlements:1700000000000", invoker.Options.CorrelationId);
    }

    [Fact]
    public async Task Client_RejectsNullRequests_BeforeTransportInvocation()
    {
        var invoker = new CapturingInvoker(new ClientVerifyPurchaseResponse());
        var client = new MonetizationBackendClient(invoker, new FixedClock());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.VerifyPurchaseAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.GetEntitlementsAsync(null!));

        Assert.Null(invoker.FunctionName);
    }

    [Fact]
    public void VerifyPurchaseResponse_ServerPayload_RoundTripsIntoShippedClientContract()
    {
        var server = new ServerVerifyPurchaseResponse
        {
            Success = true,
            TransactionKey = "google:transaction-1",
            GrantedItemIds = ["currency_coins"],
            WasDuplicate = true,
            Subscription = new SubscriptionResponseDto
            {
                ProductId = "premium_monthly",
                TierKey = "premium",
                Status = "Active",
                AutoRenew = true,
                PeriodStartUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEndUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                OriginalPurchaseDateUtc = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                Platform = "google",
                GrantedItemId = "subscription_premium",
                GracePeriodDaysRemaining = 2
            }
        };

        var client = RoundTrip<ClientVerifyPurchaseResponse>(server);

        Assert.True(client.Success);
        Assert.True(client.WasDuplicate);
        Assert.Equal("google:transaction-1", client.TransactionKey);
        Assert.Equal(["currency_coins"], client.GrantedItemIds);
        Assert.NotNull(client.Subscription);
        Assert.Equal(SubscriptionStatus.Active, client.Subscription.Status);
        Assert.Equal("google", client.Subscription.Platform);
        Assert.Equal("subscription_premium", client.Subscription.GrantedItemId);
        Assert.Equal(2, client.Subscription.GracePeriodDaysRemaining);
    }

    [Fact]
    public void GetEntitlementsResponse_ServerPayload_RoundTripsIntoShippedClientContract()
    {
        var timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var entitlementExpiry = timestamp.AddDays(7);
        var server = new ServerGetEntitlementsResponse
        {
            Entitlements =
            [
                new EntitlementItemDto
                {
                    ItemId = "remove_ads",
                    StackId = "promotional",
                    Quantity = 5_000_000_000,
                    ExpiresAtUtc = entitlementExpiry
                }
            ],
            ActiveSubscription = new ActiveSubscriptionDto
            {
                ProductId = "premium_monthly",
                TierKey = "premium",
                Status = "GracePeriod",
                PeriodStartUtc = timestamp.AddDays(-30),
                PeriodEndUtc = timestamp.AddDays(1),
                OriginalPurchaseDateUtc = timestamp.AddDays(-60),
                Platform = "apple",
                GrantedItemId = "subscription_premium",
                GracePeriodDaysRemaining = 1
            },
            ServerTimestampUtc = timestamp
        };

        var client = RoundTrip<ClientGetEntitlementsResponse>(server);

        Assert.Equal(timestamp, client.ServerTimestampUtc);
        Assert.Collection(
            client.Entitlements,
            entitlement =>
            {
                Assert.Equal("remove_ads", entitlement.ItemId);
                Assert.Equal("promotional", entitlement.StackId);
                Assert.Equal(5_000_000_000, entitlement.Quantity);
                Assert.Equal(entitlementExpiry, entitlement.ExpiresAtUtc);
            });
        Assert.NotNull(client.ActiveSubscription);
        Assert.Equal(SubscriptionStatus.GracePeriod, client.ActiveSubscription.Status);
        Assert.Equal("apple", client.ActiveSubscription.Platform);
        Assert.Equal("subscription_premium", client.ActiveSubscription.GrantedItemId);
        Assert.Equal(1, client.ActiveSubscription.GracePeriodDaysRemaining);
    }

    [Fact]
    public void ResponseEnvelope_SuccessAndFailure_IncludeAuthoritativeServerTime()
    {
        var before = DateTime.UtcNow;

        var success = MonetizationCloud::Serhat.Forge.CloudScript.Domain.DTOs.ResponseEnvelope<ServerVerifyPurchaseResponse>.Ok(
            new ServerVerifyPurchaseResponse(),
            "correlation-success",
            10);
        var failure = MonetizationCloud::Serhat.Forge.CloudScript.Domain.DTOs.ResponseEnvelope<ServerVerifyPurchaseResponse>.Fail(
            MonetizationCloud::Serhat.Forge.CloudScript.Domain.DTOs.ErrorPayload.Create(
                "FAILED",
                "Failure"),
            "correlation-failure",
            20);

        var after = DateTime.UtcNow;
        Assert.InRange(success.ServerUtcNow, before, after);
        Assert.InRange(failure.ServerUtcNow, before, after);
    }

    private static TClient RoundTrip<TClient>(object serverPayload)
    {
        var json = JsonSerializer.Serialize(serverPayload, ContractJsonOptions);
        return JsonSerializer.Deserialize<TClient>(json, ContractJsonOptions)
            ?? throw new InvalidOperationException("Contract payload deserialized to null.");
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => DateTime.UnixEpoch;
        public long TimestampMs => 1_700_000_000_000;
    }

    private sealed class CapturingInvoker : ICloudFunctionInvoker
    {
        private readonly object _response;

        public CapturingInvoker(object response)
        {
            _response = response;
        }

        public string? FunctionName { get; private set; }
        public object? Request { get; private set; }
        public CloudCallOptions? Options { get; private set; }

        public Task<CloudResult<TResponse>> ExecuteAsync<TRequest, TResponse>(
            string functionName,
            TRequest request,
            CloudCallOptions options,
            CancellationToken ct = default)
            where TRequest : class
            where TResponse : class
        {
            FunctionName = functionName;
            Request = request;
            Options = options;

            return Task.FromResult(CloudResult<TResponse>.Success((TResponse)_response));
        }
    }
}
