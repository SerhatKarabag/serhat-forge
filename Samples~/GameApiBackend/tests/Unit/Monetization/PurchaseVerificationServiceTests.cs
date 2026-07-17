using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Domain;
using Serhat.Forge.CloudScript.Framework.Monetization.Persistence;
using Serhat.Forge.CloudScript.Framework.Monetization.Services;
using Serhat.Forge.CloudScript.Framework.Monetization.Verification;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests.Monetization;

public class PurchaseVerificationServiceTests
{
    private readonly Mock<ILogger<PurchaseVerificationService>> _loggerMock;
    private readonly InMemoryPurchaseRepository _repository;
    private readonly FakeStoreVerifier _fakeVerifier;
    private readonly Mock<IEntitlementGranter> _granterMock;
    private readonly ProductAllowlistConfig _productConfig;
    private readonly PurchaseVerificationService _service;

    public PurchaseVerificationServiceTests()
    {
        _loggerMock = new Mock<ILogger<PurchaseVerificationService>>();
        _repository = new InMemoryPurchaseRepository();
        _fakeVerifier = new FakeStoreVerifier();
        _granterMock = new Mock<IEntitlementGranter>();

        _productConfig = new ProductAllowlistConfig
        {
            Products = new Dictionary<string, ProductConfig>
            {
                ["coins_100"] = new ProductConfig
                {
                    ProductId = "coins_100",
                    Type = ProductType.Consumable,
                    EconomyItemIds = new List<string> { "currency_coins" },
                    Quantity = 100,
                    Enabled = true
                },
                ["premium_monthly"] = new ProductConfig
                {
                    ProductId = "premium_monthly",
                    Type = ProductType.Subscription,
                    EconomyItemIds = new List<string> { "subscription_premium" },
                    TierKey = "premium",
                    TierPrecedence = 1,
                    Enabled = true
                },
                ["pro_monthly"] = new ProductConfig
                {
                    ProductId = "pro_monthly",
                    Type = ProductType.Subscription,
                    EconomyItemIds = new List<string> { "subscription_pro" },
                    TierKey = "pro",
                    TierPrecedence = 2,
                    Enabled = true
                }
            }
        };

        _service = new PurchaseVerificationService(
            _fakeVerifier,
            _fakeVerifier,
            _repository,
            _granterMock.Object,
            _productConfig,
            _loggerMock.Object);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_ValidConsumable_ReturnsSuccess()
    {
        // Arrange
        var request = new VerifyPurchaseServiceRequest
        {
            PlayerId = "player123",
            Platform = "apple",
            ProductId = "coins_100",
            TransactionId = "txn_001",
            ReceiptPayload = "fake_receipt"
        };

        _granterMock
            .Setup(g => g.GrantItemsAsync(It.IsAny<GrantRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "currency_coins" }));

        // Act
        var result = await _service.VerifyAndGrantAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("apple:txn_001", result.TransactionKey);
        Assert.Contains("currency_coins", result.GrantedItemIds);
        Assert.Null(result.Subscription);

        // Verify purchase was recorded
        var record = await _repository.GetPurchaseAsync("apple:txn_001");
        Assert.NotNull(record);
        Assert.Equal(PurchaseStatus.Granted, record.Status);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_ValidSubscription_ReturnsSuccessWithSubscription()
    {
        // Arrange
        var request = new VerifyPurchaseServiceRequest
        {
            PlayerId = "player123",
            Platform = "google",
            ProductId = "premium_monthly",
            TransactionId = "txn_sub_001",
            ReceiptPayload = "fake_subscription_receipt"
        };

        _granterMock
            .Setup(g => g.GrantItemsAsync(It.IsAny<GrantRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "subscription_premium" }));

        // Act
        var result = await _service.VerifyAndGrantAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Subscription);
        Assert.Equal("premium", result.Subscription.TierKey);
        Assert.Equal(SubscriptionStatus.Active, result.Subscription.Status);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_DuplicateRequest_ReturnsIdempotentResult()
    {
        // Arrange
        var request = new VerifyPurchaseServiceRequest
        {
            PlayerId = "player123",
            Platform = "apple",
            ProductId = "coins_100",
            TransactionId = "txn_dup_001",
            ReceiptPayload = "fake_receipt"
        };

        _granterMock
            .Setup(g => g.GrantItemsAsync(It.IsAny<GrantRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "currency_coins" }));

        // First request
        var firstResult = await _service.VerifyAndGrantAsync(request);
        Assert.True(firstResult.Success);
        Assert.False(firstResult.IsDuplicate);

        // Act - Second request with same transaction
        var secondResult = await _service.VerifyAndGrantAsync(request);

        // Assert
        Assert.True(secondResult.Success);
        Assert.True(secondResult.IsDuplicate);
        Assert.Contains("currency_coins", secondResult.GrantedItemIds);

        // Verify grant was only called once
        _granterMock.Verify(
            g => g.GrantItemsAsync(It.IsAny<GrantRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_ProductNotAllowed_ReturnsError()
    {
        // Arrange
        var request = new VerifyPurchaseServiceRequest
        {
            PlayerId = "player123",
            Platform = "apple",
            ProductId = "unknown_product",
            TransactionId = "txn_unknown",
            ReceiptPayload = "fake_receipt"
        };

        // Act
        var result = await _service.VerifyAndGrantAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("PRODUCT_NOT_ALLOWED", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_VerificationFails_ReturnsError()
    {
        // Arrange
        _fakeVerifier.SetFailureMode(true, "INVALID_RECEIPT", "Receipt validation failed");

        var request = new VerifyPurchaseServiceRequest
        {
            PlayerId = "player123",
            Platform = "apple",
            ProductId = "coins_100",
            TransactionId = "txn_fail_001",
            ReceiptPayload = "invalid_receipt"
        };

        // Act
        var result = await _service.VerifyAndGrantAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("INVALID_RECEIPT", result.ErrorCode);

        // Verify purchase was recorded as failed
        var record = await _repository.GetPurchaseAsync("apple:txn_fail_001");
        Assert.NotNull(record);
        Assert.Equal(PurchaseStatus.Failed, record.Status);

        // Reset
        _fakeVerifier.SetFailureMode(false, null, null);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_GrantFails_ReturnsError()
    {
        // Arrange
        var request = new VerifyPurchaseServiceRequest
        {
            PlayerId = "player123",
            Platform = "apple",
            ProductId = "coins_100",
            TransactionId = "txn_grant_fail",
            ReceiptPayload = "fake_receipt"
        };

        _granterMock
            .Setup(g => g.GrantItemsAsync(It.IsAny<GrantRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Failure("PLAYFAB_ERROR", "Failed to grant items"));

        // Act
        var result = await _service.VerifyAndGrantAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("PLAYFAB_ERROR", result.ErrorCode);

        // Verify purchase was recorded as failed
        var record = await _repository.GetPurchaseAsync("apple:txn_grant_fail");
        Assert.NotNull(record);
        Assert.Equal(PurchaseStatus.Failed, record.Status);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_DifferentPlatforms_CreatesSeparateRecords()
    {
        // Arrange
        _granterMock
            .Setup(g => g.GrantItemsAsync(It.IsAny<GrantRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "currency_coins" }));

        var appleRequest = new VerifyPurchaseServiceRequest
        {
            PlayerId = "player123",
            Platform = "apple",
            ProductId = "coins_100",
            TransactionId = "same_txn_id",
            ReceiptPayload = "apple_receipt"
        };

        var googleRequest = new VerifyPurchaseServiceRequest
        {
            PlayerId = "player123",
            Platform = "google",
            ProductId = "coins_100",
            TransactionId = "same_txn_id",
            ReceiptPayload = "google_receipt"
        };

        // Act
        var appleResult = await _service.VerifyAndGrantAsync(appleRequest);
        var googleResult = await _service.VerifyAndGrantAsync(googleRequest);

        // Assert
        Assert.True(appleResult.Success);
        Assert.True(googleResult.Success);
        Assert.Equal("apple:same_txn_id", appleResult.TransactionKey);
        Assert.Equal("google:same_txn_id", googleResult.TransactionKey);

        // Both should be new purchases (not duplicates)
        Assert.False(appleResult.IsDuplicate);
        Assert.False(googleResult.IsDuplicate);
    }
}
