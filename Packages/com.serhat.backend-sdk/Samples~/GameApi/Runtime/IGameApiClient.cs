#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;

namespace Serhat.Backend.GameApi
{
    /// <summary>
    /// High-level client interface for game-specific backend operations.
    /// </summary>
    public interface IGameApiClient : IDisposable
    {
        /// <summary>
        /// Gets bootstrap data (player progress).
        /// </summary>
        Task<CloudResult<BootstrapDto>> GetBootstrapAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets leaderboard rows for world/country scope.
        /// </summary>
        Task<CloudResult<GetLeaderboardResultDto>> GetLeaderboardAsync(
            GetLeaderboardRequestDto request,
            CancellationToken ct = default);

        /// <summary>
        /// Submits a completed level result.
        /// </summary>
        Task<CloudResult<SubmitLevelResultResultDto>> SubmitLevelResultAsync(
            SubmitLevelResultRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Synchronizes mutable player state (lives, coins, boosters, etc.).
        /// </summary>
        Task<CloudResult<SyncPlayerStateResultDto>> SyncPlayerStateAsync(
            SyncPlayerStateRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Buys lives with coins on the server side.
        /// </summary>
        Task<CloudResult<BuyLivesWithCoinsResultDto>> BuyLivesWithCoinsAsync(
            BuyLivesWithCoinsRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Grants one rewarded-ad life on the server side.
        /// </summary>
        Task<CloudResult<GrantAdRewardLifeResultDto>> GrantAdRewardLifeAsync(
            GrantAdRewardLifeRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Grants rewarded-ad coins on the server side.
        /// </summary>
        Task<CloudResult<GrantAdRewardCoinsResultDto>> GrantAdRewardCoinsAsync(
            GrantAdRewardCoinsRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Buys a start booster offer with coins on the server side.
        /// </summary>
        Task<CloudResult<BuyStartBoosterWithCoinsResultDto>> BuyStartBoosterWithCoinsAsync(
            BuyStartBoosterWithCoinsRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Buys a gameplay booster offer with coins on the server side.
        /// </summary>
        Task<CloudResult<BuyBoosterWithCoinsResultDto>> BuyBoosterWithCoinsAsync(
            BuyBoosterWithCoinsRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Grants purchase rewards on server-side.
        /// </summary>
        Task<CloudResult<GrantPurchaseRewardsResultDto>> GrantPurchaseRewardsAsync(
            GrantPurchaseRewardsRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Grants the one-time Rate Us reward on server-side.
        /// </summary>
        Task<CloudResult<ClaimRateUsRewardResultDto>> ClaimRateUsRewardAsync(
            ClaimRateUsRewardRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Claims the current calendar day's daily-gift reward and advances the streak.
        /// </summary>
        Task<CloudResult<ClaimDailyGiftResultDto>> ClaimDailyGiftAsync(
            ClaimDailyGiftRequestDto request,
            WriteOptions? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Re-stamps the caller's leaderboard metadata ("D" field) with the
        /// latest PlayFab title display name. Call after changing the display
        /// name so the leaderboard updates without waiting for a level submit.
        /// </summary>
        Task<CloudResult<RefreshLeaderboardMetadataResultDto>> RefreshLeaderboardMetadataAsync(
            CancellationToken ct = default);

        /// <summary>
        /// Gets the current outbox status.
        /// </summary>
        OutboxStatus GetOutboxStatus();

        /// <summary>
        /// Forces a flush of the outbox queue.
        /// </summary>
        Task FlushOutboxAsync(CancellationToken ct = default);
    }
}
