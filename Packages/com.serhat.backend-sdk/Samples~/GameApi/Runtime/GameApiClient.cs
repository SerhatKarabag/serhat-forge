#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.Core.Coalescing;
using Serhat.Backend.Core.Outbox;
using Serhat.Backend.Core.Resilience;

namespace Serhat.Backend.GameApi
{
    /// <summary>
    /// Game API client implementation.
    /// Provides game-specific operations using the core SDK infrastructure.
    /// </summary>
    public sealed class GameApiClient : IGameApiClient
    {
        private readonly ICloudFunctionInvoker _invoker;
        private readonly ResiliencePipeline _resilience;
        private readonly PersistentOutbox _outbox;
        private readonly OutboxFlushWorker _flushWorker;
        private readonly RequestCoalescer _coalescer;
        private readonly BackendSdkOptions _options;
        private readonly IBackendLogger _logger;
        private readonly IClock _clock;

        private bool _disposed;

        internal GameApiClient(
            ICloudFunctionInvoker invoker,
            ResiliencePipeline resilience,
            PersistentOutbox outbox,
            OutboxFlushWorker flushWorker,
            RequestCoalescer coalescer,
            BackendSdkOptions options,
            IBackendLogger logger,
            IClock clock)
        {
            _invoker = invoker;
            _resilience = resilience;
            _outbox = outbox;
            _flushWorker = flushWorker;
            _coalescer = coalescer;
            _options = options;
            _logger = logger;
            _clock = clock;
        }

        public async Task<CloudResult<BootstrapDto>> GetBootstrapAsync(CancellationToken ct = default)
        {
            var correlationId = GenerateCorrelationId();

            // Use request coalescing for reads
            return await _coalescer.ExecuteAsync(
                "GetBootstrap",
                async ct2 =>
                {
                    var options = new CloudCallOptions().WithCorrelationId(correlationId);

                    return await _resilience.ExecuteReadAsync(
                        ct3 => _invoker.ExecuteAsync<EmptyRequest, BootstrapDto>(
                            "GetBootstrap",
                            new EmptyRequest(),
                            options,
                            ct3),
                        "GetBootstrap",
                        correlationId,
                        ct: ct2);
                },
                correlationId,
                ct);
        }

        public async Task<CloudResult<GetLeaderboardResultDto>> GetLeaderboardAsync(
            GetLeaderboardRequestDto request,
            CancellationToken ct = default)
        {
            var correlationId = GenerateCorrelationId();
            var scope = request?.Scope ?? LeaderboardScopes.World;
            var pageSize = request?.PageSize ?? 100;
            var start = request?.StartingPosition ?? 1;

            return await _coalescer.ExecuteAsync(
                $"GetLeaderboard::{scope}::{pageSize}::{start}",
                async ct2 =>
                {
                    var options = new CloudCallOptions().WithCorrelationId(correlationId);

                    return await _resilience.ExecuteReadAsync(
                        ct3 => _invoker.ExecuteAsync<GetLeaderboardRequestDto, GetLeaderboardResultDto>(
                            "GetLeaderboard",
                            request ?? new GetLeaderboardRequestDto(),
                            options,
                            ct3),
                        "GetLeaderboard",
                        correlationId,
                        ct: ct2);
                },
                correlationId,
                ct);
        }

        public async Task<CloudResult<SubmitLevelResultResultDto>> SubmitLevelResultAsync(
            SubmitLevelResultRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default)
        {
            options ??= WriteOptions.Default;
            var correlationId = GenerateCorrelationId();
            var idempotencyKey = options.IdempotencyKey ?? Guid.NewGuid();

            var callOptions = new CloudCallOptions()
                .WithCorrelationId(correlationId)
                .WithIdempotencyKey(idempotencyKey);

            var result = await _resilience.ExecuteWriteAsync(
                ct2 => _invoker.ExecuteAsync<SubmitLevelResultRequestDto, SubmitLevelResultResultDto>(
                    "SubmitLevelResult",
                    request,
                    callOptions,
                    ct2),
                "SubmitLevelResult",
                correlationId,
                ct: ct);

            if (!result.IsSuccess && result.Error!.Retryable && options.AllowOutboxFallback && _options.Outbox.Enabled)
            {
                _logger.Info("[{0}] Queueing SubmitLevelResult to outbox", correlationId);
                await _outbox.EnqueueAsync(
                    "SubmitLevelResult",
                    request,
                    idempotencyKey,
                    correlationId,
                    options.OutboxPriority,
                    ct);
            }

            return result;
        }

        public async Task<CloudResult<SyncPlayerStateResultDto>> SyncPlayerStateAsync(
            SyncPlayerStateRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default)
        {
            options ??= WriteOptions.Default;
            var correlationId = GenerateCorrelationId();
            var idempotencyKey = options.IdempotencyKey ?? Guid.NewGuid();

            var callOptions = new CloudCallOptions()
                .WithCorrelationId(correlationId)
                .WithIdempotencyKey(idempotencyKey);

            var result = await _resilience.ExecuteWriteAsync(
                ct2 => _invoker.ExecuteAsync<SyncPlayerStateRequestDto, SyncPlayerStateResultDto>(
                    "SyncPlayerState",
                    request,
                    callOptions,
                    ct2),
                "SyncPlayerState",
                correlationId,
                ct: ct);

            if (!result.IsSuccess && result.Error!.Retryable && options.AllowOutboxFallback && _options.Outbox.Enabled)
            {
                _logger.Info("[{0}] Queueing SyncPlayerState to outbox", correlationId);
                await _outbox.EnqueueAsync(
                    "SyncPlayerState",
                    request,
                    idempotencyKey,
                    correlationId,
                    options.OutboxPriority,
                    ct);
            }

            return result;
        }

        public async Task<CloudResult<BuyLivesWithCoinsResultDto>> BuyLivesWithCoinsAsync(
            BuyLivesWithCoinsRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default)
        {
            options ??= WriteOptions.Default;
            var correlationId = GenerateCorrelationId();
            var idempotencyKey = options.IdempotencyKey ?? Guid.NewGuid();

            var callOptions = new CloudCallOptions()
                .WithCorrelationId(correlationId)
                .WithIdempotencyKey(idempotencyKey);

            var result = await _resilience.ExecuteWriteAsync(
                ct2 => _invoker.ExecuteAsync<BuyLivesWithCoinsRequestDto, BuyLivesWithCoinsResultDto>(
                    "BuyLivesWithCoins",
                    request,
                    callOptions,
                    ct2),
                "BuyLivesWithCoins",
                correlationId,
                ct: ct);

            if (!result.IsSuccess && result.Error!.Retryable && options.AllowOutboxFallback && _options.Outbox.Enabled)
            {
                _logger.Info("[{0}] Queueing BuyLivesWithCoins to outbox", correlationId);
                await _outbox.EnqueueAsync(
                    "BuyLivesWithCoins",
                    request,
                    idempotencyKey,
                    correlationId,
                    options.OutboxPriority,
                    ct);
            }

            return result;
        }

        public async Task<CloudResult<GrantAdRewardLifeResultDto>> GrantAdRewardLifeAsync(
            GrantAdRewardLifeRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default)
        {
            options ??= WriteOptions.Default;
            var correlationId = GenerateCorrelationId();
            var idempotencyKey = options.IdempotencyKey ?? Guid.NewGuid();

            var callOptions = new CloudCallOptions()
                .WithCorrelationId(correlationId)
                .WithIdempotencyKey(idempotencyKey);

            var result = await _resilience.ExecuteWriteAsync(
                ct2 => _invoker.ExecuteAsync<GrantAdRewardLifeRequestDto, GrantAdRewardLifeResultDto>(
                    "GrantAdRewardLife",
                    request,
                    callOptions,
                    ct2),
                "GrantAdRewardLife",
                correlationId,
                ct: ct);

            if (!result.IsSuccess && result.Error!.Retryable && options.AllowOutboxFallback && _options.Outbox.Enabled)
            {
                _logger.Info("[{0}] Queueing GrantAdRewardLife to outbox", correlationId);
                await _outbox.EnqueueAsync(
                    "GrantAdRewardLife",
                    request,
                    idempotencyKey,
                    correlationId,
                    options.OutboxPriority,
                    ct);
            }

            return result;
        }

        public async Task<CloudResult<GrantAdRewardCoinsResultDto>> GrantAdRewardCoinsAsync(
            GrantAdRewardCoinsRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default)
        {
            options ??= WriteOptions.Default;
            var correlationId = GenerateCorrelationId();
            var idempotencyKey = options.IdempotencyKey ?? Guid.NewGuid();

            var callOptions = new CloudCallOptions()
                .WithCorrelationId(correlationId)
                .WithIdempotencyKey(idempotencyKey);

            var result = await _resilience.ExecuteWriteAsync(
                ct2 => _invoker.ExecuteAsync<GrantAdRewardCoinsRequestDto, GrantAdRewardCoinsResultDto>(
                    "GrantAdRewardCoins",
                    request,
                    callOptions,
                    ct2),
                "GrantAdRewardCoins",
                correlationId,
                ct: ct);

            if (!result.IsSuccess && result.Error!.Retryable && options.AllowOutboxFallback && _options.Outbox.Enabled)
            {
                _logger.Info("[{0}] Queueing GrantAdRewardCoins to outbox", correlationId);
                await _outbox.EnqueueAsync(
                    "GrantAdRewardCoins",
                    request,
                    idempotencyKey,
                    correlationId,
                    options.OutboxPriority,
                    ct);
            }

            return result;
        }

        public async Task<CloudResult<BuyStartBoosterWithCoinsResultDto>> BuyStartBoosterWithCoinsAsync(
            BuyStartBoosterWithCoinsRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default)
        {
            options ??= WriteOptions.Default;
            var correlationId = GenerateCorrelationId();
            var idempotencyKey = options.IdempotencyKey ?? Guid.NewGuid();

            var callOptions = new CloudCallOptions()
                .WithCorrelationId(correlationId)
                .WithIdempotencyKey(idempotencyKey);

            var result = await _resilience.ExecuteWriteAsync(
                ct2 => _invoker.ExecuteAsync<BuyStartBoosterWithCoinsRequestDto, BuyStartBoosterWithCoinsResultDto>(
                    "BuyStartBoosterWithCoins",
                    request,
                    callOptions,
                    ct2),
                "BuyStartBoosterWithCoins",
                correlationId,
                ct: ct);

            if (!result.IsSuccess && result.Error!.Retryable && options.AllowOutboxFallback && _options.Outbox.Enabled)
            {
                _logger.Info("[{0}] Queueing BuyStartBoosterWithCoins to outbox", correlationId);
                await _outbox.EnqueueAsync(
                    "BuyStartBoosterWithCoins",
                    request,
                    idempotencyKey,
                    correlationId,
                    options.OutboxPriority,
                    ct);
            }

            return result;
        }

        public async Task<CloudResult<BuyBoosterWithCoinsResultDto>> BuyBoosterWithCoinsAsync(
            BuyBoosterWithCoinsRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default)
        {
            options ??= WriteOptions.Default;
            var correlationId = GenerateCorrelationId();
            var idempotencyKey = options.IdempotencyKey ?? Guid.NewGuid();

            var callOptions = new CloudCallOptions()
                .WithCorrelationId(correlationId)
                .WithIdempotencyKey(idempotencyKey);

            var result = await _resilience.ExecuteWriteAsync(
                ct2 => _invoker.ExecuteAsync<BuyBoosterWithCoinsRequestDto, BuyBoosterWithCoinsResultDto>(
                    "BuyBoosterWithCoins",
                    request,
                    callOptions,
                    ct2),
                "BuyBoosterWithCoins",
                correlationId,
                ct: ct);

            if (!result.IsSuccess && result.Error!.Retryable && options.AllowOutboxFallback && _options.Outbox.Enabled)
            {
                _logger.Info("[{0}] Queueing BuyBoosterWithCoins to outbox", correlationId);
                await _outbox.EnqueueAsync(
                    "BuyBoosterWithCoins",
                    request,
                    idempotencyKey,
                    correlationId,
                    options.OutboxPriority,
                    ct);
            }

            return result;
        }

        public async Task<CloudResult<GrantPurchaseRewardsResultDto>> GrantPurchaseRewardsAsync(
            GrantPurchaseRewardsRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default)
        {
            options ??= WriteOptions.Default;
            var correlationId = GenerateCorrelationId();
            var idempotencyKey = options.IdempotencyKey ?? Guid.NewGuid();

            var callOptions = new CloudCallOptions()
                .WithCorrelationId(correlationId)
                .WithIdempotencyKey(idempotencyKey);

            var result = await _resilience.ExecuteWriteAsync(
                ct2 => _invoker.ExecuteAsync<GrantPurchaseRewardsRequestDto, GrantPurchaseRewardsResultDto>(
                    "GrantPurchaseRewards",
                    request,
                    callOptions,
                    ct2),
                "GrantPurchaseRewards",
                correlationId,
                ct: ct);

            if (!result.IsSuccess && result.Error!.Retryable && options.AllowOutboxFallback && _options.Outbox.Enabled)
            {
                _logger.Info("[{0}] Queueing GrantPurchaseRewards to outbox", correlationId);
                await _outbox.EnqueueAsync(
                    "GrantPurchaseRewards",
                    request,
                    idempotencyKey,
                    correlationId,
                    options.OutboxPriority,
                    ct);
            }

            return result;
        }

        public async Task<CloudResult<ClaimRateUsRewardResultDto>> ClaimRateUsRewardAsync(
            ClaimRateUsRewardRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default)
        {
            options ??= WriteOptions.Default;
            var correlationId = GenerateCorrelationId();
            var idempotencyKey = options.IdempotencyKey ?? Guid.NewGuid();

            var callOptions = new CloudCallOptions()
                .WithCorrelationId(correlationId)
                .WithIdempotencyKey(idempotencyKey);

            var result = await _resilience.ExecuteWriteAsync(
                ct2 => _invoker.ExecuteAsync<ClaimRateUsRewardRequestDto, ClaimRateUsRewardResultDto>(
                    "ClaimRateUsReward",
                    request,
                    callOptions,
                    ct2),
                "ClaimRateUsReward",
                correlationId,
                ct: ct);

            if (!result.IsSuccess && result.Error!.Retryable && options.AllowOutboxFallback && _options.Outbox.Enabled)
            {
                _logger.Info("[{0}] Queueing ClaimRateUsReward to outbox", correlationId);
                await _outbox.EnqueueAsync(
                    "ClaimRateUsReward",
                    request,
                    idempotencyKey,
                    correlationId,
                    options.OutboxPriority,
                    ct);
            }

            return result;
        }

        public async Task<CloudResult<ClaimDailyGiftResultDto>> ClaimDailyGiftAsync(
            ClaimDailyGiftRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default)
        {
            options ??= WriteOptions.Default;
            var correlationId = GenerateCorrelationId();
            var idempotencyKey = options.IdempotencyKey ?? Guid.NewGuid();

            var callOptions = new CloudCallOptions()
                .WithCorrelationId(correlationId)
                .WithIdempotencyKey(idempotencyKey);

            var result = await _resilience.ExecuteWriteAsync(
                ct2 => _invoker.ExecuteAsync<ClaimDailyGiftRequestDto, ClaimDailyGiftResultDto>(
                    "ClaimDailyGift",
                    request,
                    callOptions,
                    ct2),
                "ClaimDailyGift",
                correlationId,
                ct: ct);

            if (!result.IsSuccess && result.Error!.Retryable && options.AllowOutboxFallback && _options.Outbox.Enabled)
            {
                _logger.Info("[{0}] Queueing ClaimDailyGift to outbox", correlationId);
                await _outbox.EnqueueAsync(
                    "ClaimDailyGift",
                    request,
                    idempotencyKey,
                    correlationId,
                    options.OutboxPriority,
                    ct);
            }

            return result;
        }

        public async Task<CloudResult<RefreshLeaderboardMetadataResultDto>> RefreshLeaderboardMetadataAsync(
            CancellationToken ct = default)
        {
            var correlationId = GenerateCorrelationId();
            var callOptions = new CloudCallOptions().WithCorrelationId(correlationId);

            // Read-style call: no idempotency key, no outbox fallback. The server
            // operation is naturally idempotent (re-stamping metadata with the
            // current display name produces the same result on retry).
            return await _resilience.ExecuteReadAsync(
                ct2 => _invoker.ExecuteAsync<RefreshLeaderboardMetadataRequestDto, RefreshLeaderboardMetadataResultDto>(
                    "RefreshLeaderboardMetadata",
                    new RefreshLeaderboardMetadataRequestDto(),
                    callOptions,
                    ct2),
                "RefreshLeaderboardMetadata",
                correlationId,
                ct: ct);
        }

        public OutboxStatus GetOutboxStatus()
        {
            var status = _outbox.GetStatus();
            status.LastFlushAttemptUtc = _flushWorker.LastFlushAttemptUtc;
            status.IsProcessing = _flushWorker.IsRunning;
            return status;
        }

        public async Task FlushOutboxAsync(CancellationToken ct = default)
        {
            await _flushWorker.FlushNowAsync(ct);
        }

        private string GenerateCorrelationId()
        {
            return Guid.NewGuid().ToString("N")[..8];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _flushWorker.Dispose();
            _outbox.Dispose();
            _coalescer.Dispose();
            _resilience.Dispose();
        }
    }

    /// <summary>
    /// Empty request placeholder for parameterless operations.
    /// </summary>
    [Serializable]
    internal sealed class EmptyRequest { }
}
