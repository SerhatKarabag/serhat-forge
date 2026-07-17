using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Serhat.Forge.CloudScript.Framework.Monetization.Abstractions;
using Serhat.Forge.CloudScript.Framework.Monetization.Configuration;
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
            _loggerMock.Object,
            enforceProductionSandboxPolicy: false);
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

        var renderedLogs = _loggerMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ILogger.Log))
            .Select(invocation => invocation.Arguments[2]?.ToString() ?? string.Empty);
        Assert.DoesNotContain(
            renderedLogs,
            log => log.Contains(request.TransactionId, StringComparison.Ordinal));
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
        Assert.Equal("google", result.Subscription.Platform);
        Assert.Equal("subscription_premium", result.Subscription.GrantedItemId);
        Assert.NotEqual(default, result.Subscription.OriginalPurchaseDateUtc);
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
    public async Task VerifyAndGrantAsync_UnknownPlatform_ReturnsCanonicalPlatformError()
    {
        var request = CreateConsumableRequest("txn_unknown_platform");
        request.Platform = "unknown-store";

        var result = await _service.VerifyAndGrantAsync(request);

        Assert.False(result.Success);
        Assert.Equal("INVALID_PLATFORM", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_InvalidServerGrantPayload_FailsBeforeStoreOrProvider()
    {
        _productConfig.Products["coins_100"].EconomyItemIds.Clear();
        var verifier = new Mock<IStoreVerifier>(MockBehavior.Strict);
        verifier.SetupGet(value => value.Platform).Returns("apple");
        var granter = new Mock<IEntitlementGranter>(MockBehavior.Strict);
        var service = new PurchaseVerificationService(
            verifier.Object,
            verifier.Object,
            _repository,
            granter.Object,
            _productConfig,
            _loggerMock.Object,
            enforceProductionSandboxPolicy: false);

        var result = await service.VerifyAndGrantAsync(
            CreateConsumableRequest("txn_invalid_grant_payload"));

        Assert.False(result.Success);
        Assert.Equal("PURCHASE_GRANT_SNAPSHOT_INVALID", result.ErrorCode);
        verifier.Verify(
            value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Preview")]
    public async Task VerifyAndGrantAsync_MissingOrUnknownHostEnvironment_EnforcesSandboxPolicy(
        string environmentName)
    {
        var hostConfig = new MonetizationConfig { EnvironmentName = environmentName };
        Assert.False(hostConfig.IsDevelopment);
        var granter = new Mock<IEntitlementGranter>(MockBehavior.Strict);
        var service = new PurchaseVerificationService(
            _fakeVerifier,
            _fakeVerifier,
            _repository,
            granter.Object,
            _productConfig,
            _loggerMock.Object,
            enforceProductionSandboxPolicy: !hostConfig.IsDevelopment);

        var result = await service.VerifyAndGrantAsync(
            CreateConsumableRequest($"txn_sandbox_{environmentName}"));

        Assert.False(result.Success);
        Assert.Equal("SANDBOX_NOT_ALLOWED", result.ErrorCode);
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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
    public async Task VerifyAndGrantAsync_PermanentGrantFailure_ReturnsError()
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
            .ReturnsAsync(GrantResult.Failure("INVALID_ITEM", "Configured item does not exist"));

        // Act
        var result = await _service.VerifyAndGrantAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("INVALID_ITEM", result.ErrorCode);

        // Verify purchase was recorded as failed
        var record = await _repository.GetPurchaseAsync("apple:txn_grant_fail");
        Assert.NotNull(record);
        Assert.Equal(PurchaseStatus.Failed, record.Status);
        Assert.False(record.IsRetryable);
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
        Assert.Equal(
            PurchaseRecord.CreateGoogleTransactionKey("google_receipt"),
            googleResult.TransactionKey);

        // Both should be new purchases (not duplicates)
        Assert.False(appleResult.IsDuplicate);
        Assert.False(googleResult.IsDuplicate);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_TransientStoreFailure_RetriesAfterBackoff()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("apple");
        verifier
            .SetupSequence(value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(VerificationResult.StoreError("Apple is temporarily unavailable"))
            .ReturnsAsync(ValidConsumable("coins_100", "txn_store_retry"));

        var granter = new Mock<IEntitlementGranter>();
        granter
            .Setup(value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "currency_coins" }));
        var service = CreateService(repository, verifier.Object, granter.Object, clock);
        var request = CreateConsumableRequest("txn_store_retry");

        var first = await service.VerifyAndGrantAsync(request);
        var tooEarly = await service.VerifyAndGrantAsync(request);

        Assert.False(first.Success);
        Assert.True(first.Retryable);
        Assert.Equal("STORE_ERROR", first.ErrorCode);
        Assert.False(tooEarly.Success);
        Assert.True(tooEarly.Retryable);
        verifier.Verify(
            value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var pending = await repository.GetPurchaseAsync("apple:txn_store_retry");
        Assert.NotNull(pending);
        Assert.Equal(PurchaseStatus.Pending, pending.Status);
        Assert.True(pending.IsRetryable);
        Assert.Null(pending.ProcessingLeaseId);

        clock.Advance(TimeSpan.FromSeconds(5));
        var recovered = await service.VerifyAndGrantAsync(request);

        Assert.True(recovered.Success);
        verifier.Verify(
            value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_TransientGrantFailure_ResumesWithoutReverification()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var verifier = CreateSuccessfulVerifier("txn_grant_retry");
        var granter = new Mock<IEntitlementGranter>();
        var grantIdempotencyKeys = new List<string>();
        granter
            .SetupSequence(value => value.GrantItemsAsync(
                It.Is<GrantRequest>(grant => CaptureIdempotencyKey(
                    grant,
                    grantIdempotencyKeys)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Failure("PLAYFAB_ERROR", "PlayFab returned HTTP 503"))
            .ReturnsAsync(GrantResult.Success(
                new List<string> { "currency_coins" },
                wasDuplicate: true));
        var service = CreateService(repository, verifier.Object, granter.Object, clock);
        var request = CreateConsumableRequest("txn_grant_retry");

        var first = await service.VerifyAndGrantAsync(request);

        Assert.False(first.Success);
        Assert.True(first.Retryable);
        var verified = await repository.GetPurchaseAsync("apple:txn_grant_retry");
        Assert.NotNull(verified);
        Assert.Equal(PurchaseStatus.Verified, verified.Status);
        Assert.True(verified.HasStoreVerificationSnapshot);
        Assert.True(verified.IsRetryable);

        clock.Advance(TimeSpan.FromSeconds(5));
        var recovered = await service.VerifyAndGrantAsync(request);

        Assert.True(recovered.Success);
        verifier.Verify(
            value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        Assert.Equal(2, grantIdempotencyKeys.Count);
        Assert.Equal(grantIdempotencyKeys[0], grantIdempotencyKeys[1]);
        Assert.Equal(64, grantIdempotencyKeys[0].Length);
        Assert.DoesNotContain("txn_grant_retry", grantIdempotencyKeys[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_StalePendingLease_IsReclaimed()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        await repository.CreatePurchaseAsync(CreateLeasedRecord(
            "txn_stale_pending",
            PurchaseStatus.Pending,
            clock.GetUtcNow().UtcDateTime.Subtract(TimeSpan.FromSeconds(1))));
        var verifier = CreateSuccessfulVerifier("txn_stale_pending");
        var granter = CreateSuccessfulGranter();
        var service = CreateService(repository, verifier.Object, granter.Object, clock);

        var result = await service.VerifyAndGrantAsync(
            CreateConsumableRequest("txn_stale_pending"));

        Assert.True(result.Success);
        verifier.Verify(
            value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_StaleVerifiedLease_ResumesIdempotentGrant()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var record = CreateLeasedRecord(
            "txn_stale_verified",
            PurchaseStatus.Verified,
            clock.GetUtcNow().UtcDateTime.Subtract(TimeSpan.FromSeconds(1)));
        record.HasStoreVerificationSnapshot = true;
        record.StorePurchaseDateUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-5);
        record.FirstGrantAttemptAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-5);
        await repository.CreatePurchaseAsync(record);

        var verifier = new Mock<IStoreVerifier>(MockBehavior.Strict);
        verifier.SetupGet(value => value.Platform).Returns("apple");
        var granter = CreateSuccessfulGranter(wasDuplicate: true);
        var service = CreateService(repository, verifier.Object, granter.Object, clock);

        var result = await service.VerifyAndGrantAsync(
            CreateConsumableRequest("txn_stale_verified"));

        Assert.True(result.Success);
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_ActiveLease_ReturnsRetryableInProgress()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        await repository.CreatePurchaseAsync(CreateLeasedRecord(
            "txn_active_lease",
            PurchaseStatus.Pending,
            clock.GetUtcNow().UtcDateTime.AddMinutes(1)));
        var verifier = new Mock<IStoreVerifier>(MockBehavior.Strict);
        verifier.SetupGet(value => value.Platform).Returns("apple");
        var granter = new Mock<IEntitlementGranter>(MockBehavior.Strict);
        var service = CreateService(repository, verifier.Object, granter.Object, clock);

        var result = await service.VerifyAndGrantAsync(
            CreateConsumableRequest("txn_active_lease"));

        Assert.False(result.Success);
        Assert.True(result.Retryable);
        Assert.Equal("IN_PROGRESS", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_ConcurrentRequests_OnlyLeaseOwnerProcessesPurchase()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var verificationStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseVerification = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("apple");
        verifier
            .Setup(value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                verificationStarted.TrySetResult(true);
                await releaseVerification.Task;
                return ValidConsumable("coins_100", "txn_concurrent");
            });
        var granter = CreateSuccessfulGranter();
        var service = CreateService(repository, verifier.Object, granter.Object, clock);
        var request = CreateConsumableRequest("txn_concurrent");

        var ownerTask = service.VerifyAndGrantAsync(request);
        await verificationStarted.Task;
        var contender = await service.VerifyAndGrantAsync(request);
        releaseVerification.TrySetResult(true);
        var owner = await ownerTask;

        Assert.True(owner.Success);
        Assert.False(contender.Success);
        Assert.True(contender.Retryable);
        Assert.Equal("IN_PROGRESS", contender.ErrorCode);
        verifier.Verify(
            value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_PlatformCase_DoesNotCreateSecondIdempotencyRecord()
    {
        var granter = CreateSuccessfulGranter();
        var service = new PurchaseVerificationService(
            _fakeVerifier,
            _fakeVerifier,
            _repository,
            granter.Object,
            _productConfig,
            _loggerMock.Object,
            enforceProductionSandboxPolicy: false);
        var request = CreateConsumableRequest("txn_platform_case");
        request.Platform = "APPLE";

        var first = await service.VerifyAndGrantAsync(request);
        request.Platform = "apple";
        var duplicate = await service.VerifyAndGrantAsync(request);

        Assert.True(first.Success);
        Assert.True(duplicate.Success);
        Assert.True(duplicate.IsDuplicate);
        Assert.NotNull(await _repository.GetPurchaseAsync("apple:txn_platform_case"));
        Assert.Null(await _repository.GetPurchaseAsync("APPLE:txn_platform_case"));
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_GoogleTokenIsCanonicalIdentity_RejectsCrossPlayerReplay()
    {
        const string purchaseToken = "google-token-canonical-identity";
        var granter = CreateSuccessfulGranter();
        var service = new PurchaseVerificationService(
            _fakeVerifier,
            _fakeVerifier,
            _repository,
            granter.Object,
            _productConfig,
            _loggerMock.Object,
            enforceProductionSandboxPolicy: false);
        var firstRequest = CreateGoogleConsumableRequest(
            purchaseToken,
            "untrusted-order-a",
            "player123");

        var first = await service.VerifyAndGrantAsync(firstRequest);
        var duplicate = await service.VerifyAndGrantAsync(CreateGoogleConsumableRequest(
            purchaseToken,
            "untrusted-order-b",
            "player123"));
        var conflict = await service.VerifyAndGrantAsync(CreateGoogleConsumableRequest(
            purchaseToken,
            "untrusted-order-c",
            "attacker-player"));

        var expectedKey = PurchaseRecord.CreateGoogleTransactionKey(purchaseToken);
        Assert.True(first.Success);
        Assert.Equal(expectedKey, first.TransactionKey);
        Assert.True(duplicate.Success);
        Assert.True(duplicate.IsDuplicate);
        Assert.False(conflict.Success);
        Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.ErrorCode);
        Assert.NotNull(await _repository.GetPurchaseAsync(expectedKey));
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_GoogleVerification_UsesAuthenticatedPlayerBinding()
    {
        const string playerId = "player123";
        var expectedBinding = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"serhat-forge/google-account/v1:{playerId}")));
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("google");
        verifier
            .Setup(value => value.VerifyOneTimePurchaseAsync(
                It.Is<VerifyRequest>(request =>
                    request.ExpectedObfuscatedAccountId == expectedBinding),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidConsumable("coins_100", "verified-order"));
        var granter = CreateSuccessfulGranter();
        var service = new PurchaseVerificationService(
            verifier.Object,
            verifier.Object,
            _repository,
            granter.Object,
            _productConfig,
            _loggerMock.Object,
            enforceProductionSandboxPolicy: false);

        var result = await service.VerifyAndGrantAsync(CreateGoogleConsumableRequest(
            "google-token-owner-binding",
            "untrusted-order",
            playerId));

        Assert.True(result.Success);
        verifier.Verify(
            value => value.VerifyOneTimePurchaseAsync(
                It.Is<VerifyRequest>(request =>
                    request.ExpectedObfuscatedAccountId == expectedBinding),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_AppleVerification_UsesPlayerBindingAndProductType()
    {
        const string playerId = "player123";
        const string transactionId = "apple-bound-transaction";
        var expectedToken = StoreAccountIdentity
            .CreateAppleAppAccountToken(playerId)
            .ToString("D");
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("apple");
        verifier
            .Setup(value => value.VerifyOneTimePurchaseAsync(
                It.Is<VerifyRequest>(request =>
                    request.ExpectedAppleAppAccountToken == expectedToken &&
                    request.ExpectedProductType == ProductType.Consumable &&
                    !request.IsSubscription),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidConsumable("coins_100", transactionId));
        var granter = CreateSuccessfulGranter();
        var service = new PurchaseVerificationService(
            verifier.Object,
            verifier.Object,
            _repository,
            granter.Object,
            _productConfig,
            _loggerMock.Object,
            enforceProductionSandboxPolicy: false);

        var result = await service.VerifyAndGrantAsync(
            CreateConsumableRequest(transactionId));

        Assert.True(result.Success);
        verifier.Verify(
            value => value.VerifyOneTimePurchaseAsync(
                It.Is<VerifyRequest>(request =>
                    request.ExpectedAppleAppAccountToken == expectedToken &&
                    request.ExpectedProductType == ProductType.Consumable &&
                    !request.IsSubscription),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_RetryUsesImmutableGrantSnapshotAfterCatalogMutation()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var verifier = CreateSuccessfulVerifier("txn_catalog_snapshot");
        _productConfig.Products["coins_100"].GrantMetadata = new Dictionary<string, string>
        {
            ["campaign"] = "server-original"
        };
        var capturedGrants = new List<GrantRequest>();
        var granter = new Mock<IEntitlementGranter>();
        granter
            .Setup(value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrantRequest grant, CancellationToken _) =>
            {
                capturedGrants.Add(CloneGrantRequest(grant));
                return capturedGrants.Count == 1
                    ? GrantResult.Failure("PLAYFAB_ERROR", "Temporary provider outage")
                    : GrantResult.Success(new List<string>(grant.ItemIds), wasDuplicate: true);
            });
        var service = CreateService(repository, verifier.Object, granter.Object, clock);
        var request = CreateConsumableRequest("txn_catalog_snapshot");
        var requestMetadata = new Dictionary<string, string>
        {
            ["campaign"] = "client-forged",
            ["clientOnly"] = "must-not-persist"
        };
        request.Metadata = requestMetadata;

        var first = await service.VerifyAndGrantAsync(request);
        Assert.False(first.Success);
        Assert.True(first.Retryable);

        var mutableConfig = _productConfig.Products["coins_100"];
        mutableConfig.EconomyItemIds = new List<string> { "mutated_item" };
        mutableConfig.Quantity = 1;
        mutableConfig.Enabled = false;
        mutableConfig.GrantMetadata!["campaign"] = "server-mutated";
        requestMetadata["campaign"] = "client-mutated";
        clock.Advance(TimeSpan.FromSeconds(5));

        var recovered = await service.VerifyAndGrantAsync(request);

        Assert.True(recovered.Success);
        Assert.Equal(2, capturedGrants.Count);
        Assert.All(capturedGrants, grant =>
        {
            Assert.Equal(new[] { "currency_coins" }, grant.ItemIds);
            Assert.Equal(new[] { 100 }, grant.Quantities);
            Assert.Equal("server-original", grant.Metadata!["campaign"]);
            Assert.False(grant.Metadata.ContainsKey("clientOnly"));
        });
        verifier.Verify(
            value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_NearExpiryLease_IsRenewedBeforeExternalGrant()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("apple");
        verifier
            .Setup(value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((VerifyRequest _, CancellationToken _) =>
            {
                clock.Advance(TimeSpan.FromSeconds(50));
                return ValidConsumable("coins_100", "txn_lease_renewal");
            });

        PurchaseRecord? observedDuringGrant = null;
        var granter = new Mock<IEntitlementGranter>();
        granter
            .Setup(value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                observedDuringGrant = await repository.GetPurchaseAsync(
                    "apple:txn_lease_renewal");
                return GrantResult.Success(new List<string> { "currency_coins" });
            });
        var service = CreateService(repository, verifier.Object, granter.Object, clock);

        var result = await service.VerifyAndGrantAsync(
            CreateConsumableRequest("txn_lease_renewal"));

        Assert.True(result.Success);
        Assert.NotNull(observedDuringGrant);
        Assert.Equal(
            clock.GetUtcNow().UtcDateTime.AddMinutes(1),
            observedDuringGrant.ProcessingLeaseExpiresAtUtc);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_LeaseExpiresDuringVerification_DoesNotGrant()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("apple");
        verifier
            .Setup(value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((VerifyRequest _, CancellationToken _) =>
            {
                clock.Advance(TimeSpan.FromSeconds(61));
                return ValidConsumable("coins_100", "txn_expired_owner");
            });
        var granter = new Mock<IEntitlementGranter>(MockBehavior.Strict);
        var service = CreateService(repository, verifier.Object, granter.Object, clock);

        var result = await service.VerifyAndGrantAsync(
            CreateConsumableRequest("txn_expired_owner"));

        Assert.False(result.Success);
        Assert.True(result.Retryable);
        Assert.Equal("IN_PROGRESS", result.ErrorCode);
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_SubscriptionGrantRetry_ReverifiesFreshStoreState()
    {
        const string purchaseToken = "subscription-token-reverify";
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        _productConfig.Products["premium_monthly"].EconomyItemIds.Add("subscription_bonus");
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("google");
        var refreshedExpirationUtc = clock.GetUtcNow().UtcDateTime.AddDays(30);
        verifier
            .SetupSequence(value => value.VerifySubscriptionAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidSubscription(
                "premium_monthly",
                "order-first",
                clock.GetUtcNow().UtcDateTime.AddDays(7)))
            .ReturnsAsync(ValidSubscription(
                "premium_monthly",
                "order-refreshed",
                refreshedExpirationUtc));
        var granter = new Mock<IEntitlementGranter>();
        granter
            .SetupSequence(value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Failure("PLAYFAB_ERROR", "Temporary provider outage"))
            .ReturnsAsync(GrantResult.Success(
                new List<string> { "subscription_premium" },
                wasDuplicate: true));
        var service = CreateService(repository, verifier.Object, granter.Object, clock);
        var request = new VerifyPurchaseServiceRequest
        {
            PlayerId = "player123",
            Platform = "google",
            ProductId = "premium_monthly",
            TransactionId = "untrusted-order",
            ReceiptPayload = purchaseToken
        };

        var first = await service.VerifyAndGrantAsync(request);
        Assert.False(first.Success);
        Assert.True(first.Retryable);

        clock.Advance(TimeSpan.FromSeconds(5));
        var recovered = await service.VerifyAndGrantAsync(request);

        Assert.True(recovered.Success);
        verifier.Verify(
            value => value.VerifySubscriptionAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        var subscription = await repository.GetSubscriptionAsync(
            SubscriptionRecord.CreateGoogleKey(purchaseToken));
        Assert.NotNull(subscription);
        Assert.Equal(refreshedExpirationUtc, subscription.PeriodEndUtc);
        Assert.Equal(
            new[] { "subscription_premium", "subscription_bonus" },
            subscription.ActiveEconomyItemIds);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_AppleRenewalWithSameOriginalTransaction_SkipsSecondGrant()
    {
        const string originalTransactionId = "apple-original-subscription";
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("apple");
        verifier
            .Setup(value => value.VerifySubscriptionAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((VerifyRequest request, CancellationToken _) =>
                ValidAppleSubscription(
                    request.ProductId,
                    request.TransactionId,
                    originalTransactionId,
                    request.TransactionId == "apple-renewal-1"
                        ? clock.GetUtcNow().UtcDateTime.AddDays(30)
                        : clock.GetUtcNow().UtcDateTime.AddDays(60)) with
                {
                    IsSandbox = true
                });
        var granter = new Mock<IEntitlementGranter>();
        granter
            .Setup(value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "subscription_premium" }));
        var service = CreateService(repository, verifier.Object, granter.Object, clock);

        var first = await service.VerifyAndGrantAsync(
            CreateAppleSubscriptionRequest("apple-renewal-1"));
        var renewal = await service.VerifyAndGrantAsync(
            CreateAppleSubscriptionRequest("apple-renewal-2"));

        Assert.True(first.Success);
        Assert.True(renewal.Success);
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        var subscription = await repository.GetSubscriptionAsync(
            SubscriptionRecord.CreateAppleKey(originalTransactionId));
        Assert.NotNull(subscription);
        Assert.Equal("apple-renewal-2", subscription.LatestStoreOrderId);
        Assert.True(subscription.IsSandbox);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddDays(60), subscription.PeriodEndUtc);
        var renewalPurchase = await repository.GetPurchaseAsync("apple:apple-renewal-2");
        Assert.NotNull(renewalPurchase);
        Assert.True(renewalPurchase.HasGrantAttemptTracking);
        Assert.Null(renewalPurchase.FirstGrantAttemptAtUtc);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_GoogleSecondTokenWhileSubscriptionActive_DoesNotGrant()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("google");
        verifier
            .Setup(value => value.VerifySubscriptionAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((VerifyRequest request, CancellationToken _) =>
                ValidSubscription(
                    request.ProductId,
                    $"order-{request.ReceiptPayload}",
                    clock.GetUtcNow().UtcDateTime.AddDays(30)));
        var granter = new Mock<IEntitlementGranter>();
        granter
            .Setup(value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(new List<string> { "subscription_premium" }));
        var service = CreateService(repository, verifier.Object, granter.Object, clock);

        var first = await service.VerifyAndGrantAsync(
            CreateGoogleSubscriptionRequest("google-token-a"));
        var secondToken = await service.VerifyAndGrantAsync(
            CreateGoogleSubscriptionRequest("google-token-b"));

        Assert.True(first.Success);
        Assert.False(secondToken.Success);
        Assert.False(secondToken.Retryable);
        Assert.Equal("SUBSCRIPTION_CHANGE_NOT_SUPPORTED", secondToken.ErrorCode);
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_ConcurrentAppleActivationAndRenewal_UseSameGrantKey()
    {
        const string originalTransactionId = "apple-concurrent-original";
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("apple");
        verifier
            .Setup(value => value.VerifySubscriptionAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((VerifyRequest request, CancellationToken _) =>
                ValidAppleSubscription(
                    request.ProductId,
                    request.TransactionId,
                    originalTransactionId,
                    clock.GetUtcNow().UtcDateTime.AddDays(
                        request.TransactionId == "apple-concurrent-1" ? 30 : 60)));

        var grantKeys = new List<string>();
        var bothGrantsEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var grantCount = 0;
        var granter = new Mock<IEntitlementGranter>();
        granter
            .Setup(value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (GrantRequest request, CancellationToken ct) =>
            {
                int invocation;
                lock (grantKeys)
                {
                    grantKeys.Add(request.IdempotencyKey);
                    invocation = ++grantCount;
                    if (grantCount == 2)
                    {
                        bothGrantsEntered.TrySetResult(true);
                    }
                }

                await bothGrantsEntered.Task.WaitAsync(ct);
                return GrantResult.Success(
                    new List<string> { "subscription_premium" },
                    wasDuplicate: invocation > 1);
            });
        var service = CreateService(repository, verifier.Object, granter.Object, clock);

        var activationTask = service.VerifyAndGrantAsync(
            CreateAppleSubscriptionRequest("apple-concurrent-1"));
        var renewalTask = service.VerifyAndGrantAsync(
            CreateAppleSubscriptionRequest("apple-concurrent-2"));
        var results = await Task.WhenAll(activationTask, renewalTask);

        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(2, grantKeys.Count);
        Assert.Equal(grantKeys[0], grantKeys[1]);
        var subscription = await repository.GetSubscriptionAsync(
            SubscriptionRecord.CreateAppleKey(originalTransactionId));
        Assert.NotNull(subscription);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddDays(60), subscription.PeriodEndUtc);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_SubscriptionWithoutAuthoritativeExpiry_FailsBeforeGrant()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("apple");
        verifier
            .Setup(value => value.VerifySubscriptionAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(VerificationResult.Valid() with
            {
                ProductId = "premium_monthly",
                TransactionId = "apple-missing-expiry",
                OriginalTransactionId = "apple-original-missing-expiry",
                PurchaseDateUtc = clock.GetUtcNow().UtcDateTime,
                IsSubscription = true,
                SubscriptionStatus = SubscriptionStatus.Active
            });
        var granter = new Mock<IEntitlementGranter>(MockBehavior.Strict);
        var service = CreateService(repository, verifier.Object, granter.Object, clock);

        var result = await service.VerifyAndGrantAsync(
            CreateAppleSubscriptionRequest("apple-missing-expiry"));

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Equal("SUBSCRIPTION_SNAPSHOT_INCOMPLETE", result.ErrorCode);
        Assert.Null(await repository.GetSubscriptionAsync(
            SubscriptionRecord.CreateAppleKey("apple-original-missing-expiry")));
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_RetryAfterProviderIdempotencyWindow_RequiresReconciliation()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var verifier = CreateSuccessfulVerifier("txn_reconciliation_window");
        var granter = new Mock<IEntitlementGranter>();
        granter
            .Setup(value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Failure("PLAYFAB_ERROR", "Provider response was lost"));
        var service = CreateService(repository, verifier.Object, granter.Object, clock);
        var request = CreateConsumableRequest("txn_reconciliation_window");

        var first = await service.VerifyAndGrantAsync(request);
        Assert.False(first.Success);
        Assert.True(first.Retryable);
        var firstAttempt = await repository.GetPurchaseAsync("apple:txn_reconciliation_window");
        Assert.NotNull(firstAttempt);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, firstAttempt.FirstGrantAttemptAtUtc);

        clock.Advance(TimeSpan.FromDays(13));
        var expiredRetry = await service.VerifyAndGrantAsync(request);

        Assert.False(expiredRetry.Success);
        Assert.False(expiredRetry.Retryable);
        Assert.Equal("GRANT_RECONCILIATION_REQUIRED", expiredRetry.ErrorCode);
        var failed = await repository.GetPurchaseAsync("apple:txn_reconciliation_window");
        Assert.NotNull(failed);
        Assert.Equal(PurchaseStatus.Failed, failed.Status);
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAndGrantAsync_LegacyVerifiedRowWithoutGrantTimestamp_FailsClosed()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero));
        var repository = new InMemoryPurchaseRepository();
        var record = CreateLeasedRecord(
            "txn_legacy_ambiguous_grant",
            PurchaseStatus.Verified,
            clock.GetUtcNow().UtcDateTime.AddMinutes(-1));
        record.HasStoreVerificationSnapshot = true;
        record.StorePurchaseDateUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
        await repository.CreatePurchaseAsync(record);
        var verifier = new Mock<IStoreVerifier>(MockBehavior.Strict);
        verifier.SetupGet(value => value.Platform).Returns("apple");
        var granter = new Mock<IEntitlementGranter>(MockBehavior.Strict);
        var service = CreateService(repository, verifier.Object, granter.Object, clock);

        var result = await service.VerifyAndGrantAsync(
            CreateConsumableRequest("txn_legacy_ambiguous_grant"));

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Equal("GRANT_RECONCILIATION_REQUIRED", result.ErrorCode);
        granter.Verify(
            value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void PurchaseEntity_RoundTripsImmutableGrantPayloadSnapshot()
    {
        var source = new PurchaseRecord
        {
            TransactionKey = "apple:roundtrip",
            Platform = "apple",
            ProductId = "coins_100",
            ProductType = ProductType.Consumable,
            PlayerId = "player123",
            Status = PurchaseStatus.Verified,
            HasGrantPayloadSnapshot = true,
            GrantEconomyItemIds = new List<string> { "currency_coins", "bonus_item" },
            GrantQuantities = new List<int> { 100, 100 },
            GrantMetadata = new Dictionary<string, string>
            {
                ["campaign"] = "launch",
                ["source"] = "store"
            },
            FirstGrantAttemptAtUtc = new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc),
            HasGrantAttemptTracking = true,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc),
            StoreTransactionId = "roundtrip"
        };

        var restored = PurchaseEntity.FromRecord(source, "title").ToRecord();

        Assert.True(restored.HasGrantPayloadSnapshot);
        Assert.Equal(source.GrantEconomyItemIds, restored.GrantEconomyItemIds);
        Assert.Equal(source.GrantQuantities, restored.GrantQuantities);
        Assert.Equal(source.GrantMetadata, restored.GrantMetadata);
        Assert.Equal(source.FirstGrantAttemptAtUtc, restored.FirstGrantAttemptAtUtc);
        Assert.Equal(source.HasGrantAttemptTracking, restored.HasGrantAttemptTracking);
    }

    private PurchaseVerificationService CreateService(
        InMemoryPurchaseRepository repository,
        IStoreVerifier verifier,
        IEntitlementGranter granter,
        TimeProvider timeProvider) =>
        new(
            verifier,
            verifier,
            repository,
            granter,
            _productConfig,
            _loggerMock.Object,
            enforceProductionSandboxPolicy: false,
            timeProvider: timeProvider,
            processingLeaseDuration: TimeSpan.FromMinutes(1),
            baseRetryDelay: TimeSpan.FromSeconds(5));

    private static VerifyPurchaseServiceRequest CreateConsumableRequest(string transactionId) =>
        new()
        {
            PlayerId = "player123",
            Platform = "apple",
            ProductId = "coins_100",
            TransactionId = transactionId,
            ReceiptPayload = "fake_receipt"
        };

    private static VerifyPurchaseServiceRequest CreateGoogleConsumableRequest(
        string purchaseToken,
        string transactionId,
        string playerId) =>
        new()
        {
            PlayerId = playerId,
            Platform = "google",
            ProductId = "coins_100",
            TransactionId = transactionId,
            ReceiptPayload = purchaseToken
        };

    private static VerifyPurchaseServiceRequest CreateAppleSubscriptionRequest(
        string transactionId) =>
        new()
        {
            PlayerId = "player123",
            Platform = "apple",
            ProductId = "premium_monthly",
            TransactionId = transactionId,
            ReceiptPayload = string.Empty
        };

    private static VerifyPurchaseServiceRequest CreateGoogleSubscriptionRequest(
        string purchaseToken) =>
        new()
        {
            PlayerId = "player123",
            Platform = "google",
            ProductId = "premium_monthly",
            TransactionId = string.Empty,
            ReceiptPayload = purchaseToken
        };

    private static GrantRequest CloneGrantRequest(GrantRequest source) =>
        new()
        {
            PlayerId = source.PlayerId,
            ItemIds = new List<string>(source.ItemIds),
            Quantities = source.Quantities == null
                ? null
                : new List<int>(source.Quantities),
            IdempotencyKey = source.IdempotencyKey,
            Metadata = source.Metadata == null
                ? null
                : new Dictionary<string, string>(source.Metadata, StringComparer.Ordinal)
        };

    private static VerificationResult ValidConsumable(string productId, string transactionId) =>
        VerificationResult.Valid() with
        {
            ProductId = productId,
            TransactionId = transactionId,
            PurchaseDateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static VerificationResult ValidSubscription(
        string productId,
        string transactionId,
        DateTime expirationUtc) =>
        VerificationResult.Valid() with
        {
            ProductId = productId,
            TransactionId = transactionId,
            PurchaseDateUtc = expirationUtc.AddDays(-30),
            ExpirationDateUtc = expirationUtc,
            IsSubscription = true,
            SubscriptionStatus = SubscriptionStatus.Active,
            AutoRenew = true
        };

    private static VerificationResult ValidAppleSubscription(
        string productId,
        string transactionId,
        string originalTransactionId,
        DateTime expirationUtc) =>
        ValidSubscription(productId, transactionId, expirationUtc) with
        {
            OriginalTransactionId = originalTransactionId
        };

    private static Mock<IStoreVerifier> CreateSuccessfulVerifier(string transactionId)
    {
        var verifier = new Mock<IStoreVerifier>();
        verifier.SetupGet(value => value.Platform).Returns("apple");
        verifier
            .Setup(value => value.VerifyOneTimePurchaseAsync(
                It.IsAny<VerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidConsumable("coins_100", transactionId));
        return verifier;
    }

    private static Mock<IEntitlementGranter> CreateSuccessfulGranter(bool wasDuplicate = false)
    {
        var granter = new Mock<IEntitlementGranter>();
        granter
            .Setup(value => value.GrantItemsAsync(
                It.IsAny<GrantRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrantResult.Success(
                new List<string> { "currency_coins" },
                wasDuplicate));
        return granter;
    }

    private static bool CaptureIdempotencyKey(
        GrantRequest request,
        ICollection<string> destination)
    {
        destination.Add(request.IdempotencyKey);
        return true;
    }

    private static PurchaseRecord CreateLeasedRecord(
        string transactionId,
        PurchaseStatus status,
        DateTime leaseExpiryUtc) =>
        new()
        {
            TransactionKey = $"apple:{transactionId}",
            Platform = "apple",
            ProductId = "coins_100",
            ProductType = ProductType.Consumable,
            PlayerId = "player123",
            Status = status,
            CreatedAtUtc = leaseExpiryUtc.AddMinutes(-1),
            UpdatedAtUtc = leaseExpiryUtc.AddMinutes(-1),
            StoreTransactionId = transactionId,
            ProcessingLeaseId = "abandoned-worker",
            ProcessingLeaseExpiresAtUtc = leaseExpiryUtc,
            AttemptCount = 1
        };

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
