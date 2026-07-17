using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Domain;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests.Unit.Monetization;

public sealed class RestoreResultContractTests
{
    [Fact]
    public void NoRestorations_IsSuccessfulTerminalOutcome()
    {
        var result = RestoreResult.NoRestorations();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsPartialSuccess);
        Assert.Equal(RestoreResultStatus.NoPurchases, result.Status);
        Assert.Empty(result.RestoredPurchases);
        Assert.Empty(result.FailedPurchases);
    }

    [Fact]
    public void FromPurchases_WithMixedResults_IsPartialAndSeparatesFailures()
    {
        var restored = PurchaseResult.Restored("remove_ads", "tx-1", ["remove_ads"]);
        var failed = PurchaseResult.Failure(
            "premium",
            PurchaseError.VerificationFailed("invalid receipt"));

        var result = RestoreResult.FromPurchases([restored, failed]);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsPartialSuccess);
        Assert.Equal(RestoreResultStatus.PartiallySucceeded, result.Status);
        Assert.Equal([restored], result.RestoredPurchases);
        Assert.Equal([failed], result.FailedPurchases);
        Assert.Same(failed.Error, result.Error);
    }

    [Fact]
    public void FromPurchases_WhenEveryReceiptFails_IsFailureNotEmptySuccess()
    {
        var failure = PurchaseResult.Failure(
            "premium",
            PurchaseError.StoreUnavailable("store unavailable"));

        var result = RestoreResult.FromPurchases([failure]);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsPartialSuccess);
        Assert.Equal(RestoreResultStatus.Failed, result.Status);
        Assert.Empty(result.RestoredPurchases);
        Assert.Single(result.FailedPurchases);
    }

    [Fact]
    public void StoreRestoreResult_WithMixedTranslationOutcome_IsPartial()
    {
        var receipt = new StoreReceipt
        {
            Platform = "apple",
            ProductId = "remove_ads",
            TransactionId = "tx-1",
            ReceiptPayload = "receipt"
        };
        var error = PurchaseError.VerificationFailed("incomplete receipt");

        var result = StoreRestoreResult.Partial([receipt], [error]);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsPartialSuccess);
        Assert.Equal(StoreRestoreStatus.PartiallySucceeded, result.Status);
        Assert.Equal([receipt], result.Receipts);
        Assert.Equal([error], result.Errors);
    }

    [Fact]
    public void PurchaseResult_SnapshotsGrantedItems()
    {
        var grantedItems = new List<string> { "coins_100" };
        var result = PurchaseResult.Success("coins", "tx-1", grantedItems);

        grantedItems.Add("unexpected");

        Assert.Equal(["coins_100"], result.GrantedItemIds);
    }
}
