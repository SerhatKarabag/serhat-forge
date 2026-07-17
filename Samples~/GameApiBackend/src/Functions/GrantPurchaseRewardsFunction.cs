using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Serhat.Forge.CloudScript.Functions;

/// <summary>
/// Disabled legacy endpoint. Receipt-shape checks are not purchase verification.
/// Deploy and register the hardened monetization Function App instead.
/// </summary>
[Obsolete("Use the hardened monetization Function App endpoint instead.")]
public sealed class GrantPurchaseRewardsFunction
{
    [Function("GrantPurchaseRewards")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.Gone);
        await response.WriteAsJsonAsync(new
        {
            success = false,
            error = new
            {
                code = "LEGACY_MONETIZATION_DISABLED",
                message = "Use the hardened monetization Function App endpoint."
            }
        });
        return response;
    }
}