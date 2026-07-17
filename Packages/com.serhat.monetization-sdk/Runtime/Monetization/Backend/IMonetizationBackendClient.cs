using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;

namespace Serhat.Backend.Monetization.Backend
{
    /// <summary>
    /// Backend client for monetization operations.
    /// Uses the existing backend SDK invoker and resilience patterns.
    /// </summary>
    public interface IMonetizationBackendClient
    {
        /// <summary>
        /// Verifies a purchase with the server.
        /// Server will validate with Apple/Google and grant entitlements.
        /// </summary>
        /// <param name="request">Verification request.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Verification result.</returns>
        Task<CloudResult<VerifyPurchaseResponse>> VerifyPurchaseAsync(
            VerifyPurchaseRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Gets player entitlements from the server.
        /// </summary>
        /// <param name="request">Request options.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Entitlements response.</returns>
        Task<CloudResult<GetEntitlementsResponse>> GetEntitlementsAsync(
            GetEntitlementsRequest request,
            CancellationToken ct = default);
    }
}
