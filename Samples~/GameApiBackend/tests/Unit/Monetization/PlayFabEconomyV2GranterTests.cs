extern alias MonetizationCloud;

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using InventoryQueryResult = MonetizationCloud::Serhat.Forge.CloudScript.Framework.Monetization.Abstractions.InventoryQueryResult;
using PlayFabEconomyV2Granter = MonetizationCloud::Serhat.Forge.CloudScript.Framework.Monetization.PlayFab.PlayFabEconomyV2Granter;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public sealed class PlayFabEconomyV2GranterTests
{
    [Fact]
    public async Task GetPlayerItemsAsync_UsesTitleEntityToken_AndPaginatesCompleteInventory()
    {
        var requests = new ConcurrentQueue<CapturedRequest>();
        var inventoryCall = 0;
        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            requests.Enqueue(await CapturedRequest.FromAsync(request, ct));
            if (request.RequestUri!.AbsolutePath.EndsWith("/Authentication/GetEntityToken", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {"data":{"EntityToken":"title-token","TokenExpiration":"2099-01-01T00:00:00Z"}}
                    """);
            }

            inventoryCall++;
            return inventoryCall == 1
                ? Json(HttpStatusCode.OK,
                    """
                    {"data":{"Items":[
                      {"Id":"coins","StackId":"default","Amount":100},
                      {"Id":"expired","StackId":"old","Amount":1,"ExpirationDate":"2020-01-01T00:00:00Z"}
                    ],"ContinuationToken":"next-page"}}
                    """)
                : Json(HttpStatusCode.OK,
                    """
                    {"data":{"items":[
                      {"id":"boost","stackId":"promo","amount":2,"expirationDate":"2099-02-01T00:00:00Z"},
                      {"id":"empty","stackId":"default","amount":0}
                    ]}}
                    """);
        });

        using var granter = CreateGranter(handler);

        var result = await granter.GetPlayerItemsAsync("player-1");

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Items,
            coins =>
            {
                Assert.Equal("coins", coins.ItemId);
                Assert.Equal("default", coins.StackId);
                Assert.Equal(100, coins.Amount);
                Assert.Null(coins.ExpiresAtUtc);
            },
            boost =>
            {
                Assert.Equal("boost", boost.ItemId);
                Assert.Equal("promo", boost.StackId);
                Assert.Equal(2, boost.Amount);
                Assert.Equal(new DateTime(2099, 2, 1, 0, 0, 0, DateTimeKind.Utc), boost.ExpiresAtUtc);
            });

        var captured = requests.ToArray();
        Assert.Equal(3, captured.Length);
        Assert.True(captured[0].Headers.ContainsKey("X-SecretKey"));
        Assert.False(captured[0].Headers.ContainsKey("X-EntityToken"));
        Assert.All(captured.Skip(1), request =>
        {
            Assert.False(request.Headers.ContainsKey("X-SecretKey"));
            Assert.Equal("title-token", request.Headers["X-EntityToken"]);
        });
        Assert.Contains("\"continuationToken\":null", captured[1].Body, StringComparison.Ordinal);
        Assert.Contains("\"continuationToken\":\"next-page\"", captured[2].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPlayerItemsAsync_ProviderFailure_IsNotReportedAsEmptyInventory()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/Authentication/GetEntityToken", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(HttpStatusCode.OK,
                    """{"data":{"EntityToken":"title-token","TokenExpiration":"2099-01-01T00:00:00Z"}}"""));
            }

            return Task.FromResult(Json(
                HttpStatusCode.ServiceUnavailable,
                """{"error":"ServiceUnavailable","errorMessage":"temporary"}"""));
        });

        using var granter = CreateGranter(handler);

        InventoryQueryResult result = await granter.GetPlayerItemsAsync("player-1");

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.True(result.IsRetryable);
        Assert.Equal("ServiceUnavailable", result.ErrorCode);
    }

    [Fact]
    public async Task GetPlayerItemsAsync_RejectedToken_RefreshesOnceWithoutLeakingSecretToInventory()
    {
        var authCalls = 0;
        var inventoryCalls = 0;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/Authentication/GetEntityToken", StringComparison.Ordinal))
            {
                authCalls++;
                var token = authCalls == 1 ? "stale-token" : "fresh-token";
                return Task.FromResult(Json(HttpStatusCode.OK,
                    JsonSerializer.Serialize(new
                    {
                        data = new
                        {
                            EntityToken = token,
                            TokenExpiration = "2099-01-01T00:00:00Z"
                        }
                    })));
            }

            inventoryCalls++;
            Assert.False(request.Headers.Contains("X-SecretKey"));
            return Task.FromResult(inventoryCalls == 1
                ? Json(HttpStatusCode.Unauthorized, """{"error":"InvalidEntityToken"}""")
                : Json(HttpStatusCode.OK, """{"data":{"Items":[]}}"""));
        });

        using var granter = CreateGranter(handler);

        var result = await granter.GetPlayerItemsAsync("player-1");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.Equal(2, authCalls);
        Assert.Equal(2, inventoryCalls);
    }

    [Fact]
    public async Task GetPlayerItemsAsync_MalformedSuccessPayload_FailsClosed()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/Authentication/GetEntityToken", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK,
                    """{"data":{"EntityToken":"title-token","TokenExpiration":"2099-01-01T00:00:00Z"}}""")
                : Json(HttpStatusCode.OK, """{"data":{"ContinuationToken":null}}""")));

        using var granter = CreateGranter(handler);

        var result = await granter.GetPlayerItemsAsync("player-1");

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_PROVIDER_RESPONSE", result.ErrorCode);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public async Task GetPlayerItemsAsync_ConcurrentQueries_ShareCachedEntityToken()
    {
        var authCalls = 0;
        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/Authentication/GetEntityToken", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref authCalls);
                await Task.Delay(25, ct);
                return Json(HttpStatusCode.OK,
                    """{"data":{"EntityToken":"title-token","TokenExpiration":"2099-01-01T00:00:00Z"}}""");
            }

            return Json(HttpStatusCode.OK, """{"data":{"Items":[]}}""");
        });

        using var granter = CreateGranter(handler);

        var results = await Task.WhenAll(
            granter.GetPlayerItemsAsync("player-1"),
            granter.GetPlayerItemsAsync("player-2"));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(1, authCalls);
    }

    private static PlayFabEconomyV2Granter CreateGranter(HttpMessageHandler handler) =>
        new(
            "TEST",
            "top-secret",
            NullLogger<PlayFabEconomyV2Granter>.Instance,
            new HttpClient(handler));

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private sealed record CapturedRequest(
        IReadOnlyDictionary<string, string> Headers,
        string Body)
    {
        public static async Task<CapturedRequest> FromAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            var headers = request.Headers.ToDictionary(
                pair => pair.Key,
                pair => string.Join(",", pair.Value),
                StringComparer.OrdinalIgnoreCase);
            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(ct);
            return new CapturedRequest(headers, body);
        }
    }
}
