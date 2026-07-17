#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;

namespace Serhat.Backend.Monetization.Backend
{
    /// <summary>
    /// Backend client implementation for monetization operations.
    /// Uses ICloudFunctionInvoker for server communication.
    /// </summary>
    public sealed class MonetizationBackendClient : IMonetizationBackendClient
    {
        private const string VerifyPurchaseFunctionName = "VerifyPurchase";
        private const string GetEntitlementsFunctionName = "GetEntitlements";

        private readonly ICloudFunctionInvoker _invoker;
        private readonly IClock _clock;

        public MonetizationBackendClient(
            ICloudFunctionInvoker invoker,
            IClock clock)
        {
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public async Task<CloudResult<VerifyPurchaseResponse>> VerifyPurchaseAsync(
            VerifyPurchaseRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var operationId = Guid.NewGuid();
            var options = new CloudCallOptions()
                .WithIdempotencyKey(operationId)
                // A Google transaction ID is the purchase token. Correlation IDs can
                // be exported to logs and tracing systems, so keep them opaque.
                .WithCorrelationId($"verify:{_clock.TimestampMs}:{operationId:N}");

            return await _invoker.ExecuteAsync<VerifyPurchaseRequest, VerifyPurchaseResponse>(
                VerifyPurchaseFunctionName, request, options, ct);
        }

        public async Task<CloudResult<GetEntitlementsResponse>> GetEntitlementsAsync(
            GetEntitlementsRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var options = new CloudCallOptions()
                .WithCorrelationId($"entitlements:{_clock.TimestampMs}");

            return await _invoker.ExecuteAsync<GetEntitlementsRequest, GetEntitlementsResponse>(
                GetEntitlementsFunctionName, request, options, ct);
        }
    }

    /// <summary>
    /// Builder for MonetizationBackendClient.
    /// </summary>
    public sealed class MonetizationBackendClientBuilder
    {
        private ICloudFunctionInvoker? _invoker;
        private IClock? _clock;

        public MonetizationBackendClientBuilder WithInvoker(ICloudFunctionInvoker invoker)
        {
            _invoker = invoker;
            return this;
        }

        public MonetizationBackendClientBuilder WithClock(IClock clock)
        {
            _clock = clock;
            return this;
        }

        public MonetizationBackendClient Build()
        {
            if (_invoker == null)
            {
                throw new InvalidOperationException(
                    "Invoker is required. Call WithInvoker() before Build().");
            }

            var clock = _clock ?? SystemClock.Instance;
            return new MonetizationBackendClient(_invoker, clock);
        }
    }
}
