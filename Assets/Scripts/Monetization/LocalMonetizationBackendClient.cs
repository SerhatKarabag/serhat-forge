#if SERHAT_FORGE_LOCAL_MONETIZATION && (UNITY_EDITOR || DEVELOPMENT_BUILD)
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.Monetization.Backend;
using Serhat.Backend.Monetization.Domain;

namespace Serhat.Forge.Monetization
{
    /// <summary>
    /// Explicitly opt-in local backend stub for editor/development-build smoke tests.
    /// This type is intentionally absent from non-development players; production
    /// purchase flows must use MonetizationBackendClient plus verified cloud endpoints.
    /// </summary>
    public sealed class LocalMonetizationBackendClient : IMonetizationBackendClient
    {
        public Task<CloudResult<VerifyPurchaseResponse>> VerifyPurchaseAsync(
            VerifyPurchaseRequest request,
            CancellationToken ct = default)
        {
            var response = new VerifyPurchaseResponse
            {
                Success = true,
                TransactionKey = request.TransactionId,
                GrantedItemIds = new List<string> { $"local_{request.ProductId}" }
            };

            if (request.ProductType == "Subscription")
            {
                response.Subscription = new SubscriptionDto
                {
                    ProductId = request.ProductId,
                    TierKey = request.TierKey ?? "",
                    Status = SubscriptionStatus.Active,
                    AutoRenew = true,
                    PeriodStartUtc = DateTime.UtcNow,
                    PeriodEndUtc = DateTime.UtcNow.AddMonths(1),
                    OriginalPurchaseDateUtc = DateTime.UtcNow
                };
            }

            return Task.FromResult(CloudResult<VerifyPurchaseResponse>.Success(response));
        }

        public Task<CloudResult<GetEntitlementsResponse>> GetEntitlementsAsync(
            GetEntitlementsRequest request,
            CancellationToken ct = default)
        {
            var response = new GetEntitlementsResponse
            {
                Entitlements = new List<EntitlementDto>(),
                ServerTimestampUtc = DateTime.UtcNow
            };

            return Task.FromResult(CloudResult<GetEntitlementsResponse>.Success(response));
        }
    }
}
#endif
