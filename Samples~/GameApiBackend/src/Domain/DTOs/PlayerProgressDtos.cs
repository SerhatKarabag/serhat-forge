using System;
using System.Collections.Generic;

namespace Serhat.Forge.CloudScript.Domain.DTOs;

/// <summary>
/// Player progression for a level-based game.
/// </summary>
public sealed class PlayerProgressDto
{
    public int SchemaVersion { get; set; } = 1;
    public long StateVersion { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public int CurrentLevel { get; set; } = 1;
    public int Lives { get; set; } = 5;
    public int MaxLives { get; set; } = 5;
    public int Coins { get; set; } = 100;
    public int TotalCoinsEarned { get; set; }
    public int Stars { get; set; }
    public bool HasInfiniteLives { get; set; }
    public DateTime InfiniteLivesEndUtc { get; set; }
    public DateTime NextLifeTimeUtc { get; set; }
    public int WinStreak { get; set; }
    public bool HasRemovedAds { get; set; }
    public Dictionary<string, int> BoostersOwned { get; set; } = new();
    public Dictionary<string, int> BoostersFree { get; set; } = new();
    public Dictionary<string, VerifiedPurchaseRecordDto> VerifiedPurchases { get; set; } = new();
    public SubscriptionDto? ActiveSubscription { get; set; }
    public Dictionary<string, DateTime> ProcessedPurchaseTransactions { get; set; } = new();
    public Dictionary<string, DateTime> ClaimedRewards { get; set; } = new();
    public int PiggyBankCoins { get; set; }
    public DateTime PiggyBankStartedUtc { get; set; }
    public int PiggyBankDurationSeconds { get; set; }
    public int PiggyBankMaxCoins { get; set; }
    public CrownEventStateDto CrownEvent { get; set; } = new();
    public int DailyGiftStreakDay { get; set; }
    public DateTime DailyGiftLastClaimedUtc { get; set; }
    public Dictionary<string, LevelResultDto> Results { get; set; } = new();
    public DateTime LastUpdatedUtc { get; set; }
}

/// <summary>
/// Result for a completed level.
/// </summary>
public sealed class LevelResultDto
{
    public int Stars { get; set; }
    public float TimeSec { get; set; }
}

/// <summary>
/// Bootstrap payload: player progress.
/// </summary>
public sealed class BootstrapDto
{
    public PlayerProgressDto Progress { get; set; } = new();
    public EconomyConfigDto Economy { get; set; } = new();
    public CrownEventConfigDto CrownEvent { get; set; } = new();
    public GameplayBalanceConfigDto GameplayBalance { get; set; } = new();
    public DailyGiftConfigDto DailyGift { get; set; } = new();
}

/// <summary>
/// Current state of crown event progression for the player.
/// </summary>
public sealed class CrownEventStateDto
{
    public int CycleIndex { get; set; }
    public int CrownsInCycle { get; set; }
    public List<int> ClaimedMilestones { get; set; } = new();
    public DateTime CycleStartedUtc { get; set; }
    public int CycleDurationSeconds { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}

/// <summary>
/// Server-authoritative crown event configuration.
/// </summary>
public sealed class CrownEventConfigDto
{
    public int CrownsPerCycle { get; set; }
    public int CycleDurationSeconds { get; set; }
    public List<CrownEventMilestoneDto> Milestones { get; set; } = new();
}

/// <summary>
/// Single crown event milestone definition.
/// </summary>
public sealed class CrownEventMilestoneDto
{
    public int MilestoneIndex { get; set; }
    public int RequiredCrowns { get; set; }
    public int Coins { get; set; }
    public int InfiniteLivesMinutes { get; set; }
    public Dictionary<string, int> Boosters { get; set; } = new();
}

/// <summary>
/// Server-authoritative economy settings needed by the client UI.
/// </summary>
public sealed class EconomyConfigDto
{
    public int CoinCostPerLife { get; set; }
    public List<BoosterOfferDto> BoosterOffers { get; set; } = new();
    public List<StartBoosterOfferDto> StartBoosterOffers { get; set; } = new();
}

/// <summary>
/// Remote gameplay balance values loaded from PlayFab Title Data.
/// Only contains asset-free values that are safe to override at runtime.
/// </summary>
public sealed class GameplayBalanceConfigDto
{
    public int Version { get; set; } = 1;
    public float? Speed { get; set; }
    public float? Smoothness { get; set; }
    public float? HoleRotationSmoothness { get; set; }
    public int? LifeRegenTimeSeconds { get; set; }
    public int? MaxLives { get; set; }
    public int? StartingLives { get; set; }
    public int? StartingCoins { get; set; }
    public int? InterstitialAfterLevel { get; set; }
    public GameplayFeatureGateRuleDto[] FeatureGates { get; set; } = Array.Empty<GameplayFeatureGateRuleDto>();
    public int[] SizeXp { get; set; } = Array.Empty<int>();
    public float[] HoleScaleMultipliers { get; set; } = Array.Empty<float>();
    public float[] HoleSpeedMultipliers { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Optional remote overrides for a single feature gate.
/// </summary>
public sealed class GameplayFeatureGateRuleDto
{
    public string FeatureId { get; set; } = string.Empty;
    public int? UnlockLevel { get; set; }
    public bool? EnableNotification { get; set; }
    public bool? HideWhenNoAdsOwned { get; set; }
}

/// <summary>
/// Server-authoritative gameplay booster offer shown in the in-game purchase popup.
/// </summary>
public sealed class BoosterOfferDto
{
    public string BoosterType { get; set; } = string.Empty;
    public int CoinPrice { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// Server-authoritative start booster offer shown in the pre-game purchase popup.
/// </summary>
public sealed class StartBoosterOfferDto
{
    public string BoosterType { get; set; } = string.Empty;
    public int CoinPrice { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// Request to submit a completed level.
/// </summary>
public sealed class SubmitLevelResultRequestDto
{
    public int LevelId { get; set; }
    public float TimeSec { get; set; }
    public int Stars { get; set; }
    public int CrownsCollected { get; set; }
}

/// <summary>
/// Result of submitting a completed level.
/// </summary>
public sealed class SubmitLevelResultResultDto
{
    public bool Success { get; set; }
    public int NewCurrentLevel { get; set; }
    public PlayerProgressDto? UpdatedProgress { get; set; }
}

/// <summary>
/// Request to synchronize mutable player state (lives/coins/boosters, etc.).
/// </summary>
public sealed class SyncPlayerStateRequestDto
{
    public PlayerProgressDto Progress { get; set; } = new();
}

/// <summary>
/// Result of synchronizing mutable player state.
/// </summary>
public sealed class SyncPlayerStateResultDto
{
    public bool Success { get; set; }
    public PlayerProgressDto UpdatedProgress { get; set; } = new();
}

/// <summary>
/// Request to buy lives with coins. If LivesToBuy is not set, backend refills all missing lives.
/// </summary>
public sealed class BuyLivesWithCoinsRequestDto
{
    public int? LivesToBuy { get; set; }
}

/// <summary>
/// Result of buying lives with coins.
/// </summary>
public sealed class BuyLivesWithCoinsResultDto
{
    public bool Success { get; set; }
    public int CoinsSpent { get; set; }
    public int LivesGranted { get; set; }
    public PlayerProgressDto UpdatedProgress { get; set; } = new();
}

/// <summary>
/// Request to grant a rewarded-ad life.
/// </summary>
public sealed class GrantAdRewardLifeRequestDto
{
    public string RewardToken { get; set; } = string.Empty;
}

/// <summary>
/// Result of granting a rewarded-ad life.
/// </summary>
public sealed class GrantAdRewardLifeResultDto
{
    public bool Success { get; set; }
    public int LivesGranted { get; set; }
    public PlayerProgressDto UpdatedProgress { get; set; } = new();
}

/// <summary>
/// Request to grant rewarded-ad coins.
/// </summary>
public sealed class GrantAdRewardCoinsRequestDto
{
    public string RewardToken { get; set; } = string.Empty;
}

/// <summary>
/// Result of granting rewarded-ad coins.
/// </summary>
public sealed class GrantAdRewardCoinsResultDto
{
    public bool Success { get; set; }
    public int CoinsGranted { get; set; }
    public PlayerProgressDto UpdatedProgress { get; set; } = new();
}

/// <summary>
/// Request to buy a start booster offer with coins.
/// </summary>
public sealed class BuyStartBoosterWithCoinsRequestDto
{
    public string BoosterType { get; set; } = string.Empty;
}

/// <summary>
/// Result of buying a start booster offer with coins.
/// </summary>
public sealed class BuyStartBoosterWithCoinsResultDto
{
    public bool Success { get; set; }
    public string BoosterType { get; set; } = string.Empty;
    public int CoinsSpent { get; set; }
    public int BoostersGranted { get; set; }
    public PlayerProgressDto UpdatedProgress { get; set; } = new();
}

/// <summary>
/// Request to buy a gameplay booster offer with coins.
/// </summary>
public sealed class BuyBoosterWithCoinsRequestDto
{
    public string BoosterType { get; set; } = string.Empty;
}

/// <summary>
/// Result of buying a gameplay booster offer with coins.
/// </summary>
public sealed class BuyBoosterWithCoinsResultDto
{
    public bool Success { get; set; }
    public string BoosterType { get; set; } = string.Empty;
    public int CoinsSpent { get; set; }
    public int BoostersGranted { get; set; }
    public PlayerProgressDto UpdatedProgress { get; set; } = new();
}

/// <summary>
/// Request to grant purchase rewards.
 /// </summary>
public sealed class GrantPurchaseRewardsRequestDto
{
    public string ProductId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string TierKey { get; set; } = string.Empty;
    public bool WasRestored { get; set; }
}

/// <summary>
/// Result of granting purchase rewards.
/// </summary>
public sealed class GrantPurchaseRewardsResultDto
{
    public bool Success { get; set; }
    public bool WasDuplicate { get; set; }
    public PlayerProgressDto UpdatedProgress { get; set; } = new();
}

/// <summary>
/// Server-authoritative daily-gift configuration. Ordered by <see cref="DailyGiftRewardDto.Day"/> (1..Length).
/// </summary>
public sealed class DailyGiftConfigDto
{
    public List<DailyGiftRewardDto> Rewards { get; set; } = new();
}

/// <summary>
/// Reward definition for a single daily-gift day.
/// </summary>
public sealed class DailyGiftRewardDto
{
    public int Day { get; set; }
    public int Coins { get; set; }
    public int InfiniteLivesMinutes { get; set; }
    public Dictionary<string, int> Boosters { get; set; } = new();
}

/// <summary>
/// Request to claim the current day's daily gift.
/// </summary>
public sealed class ClaimDailyGiftRequestDto
{
}

/// <summary>
/// Result of claiming the daily gift.
/// </summary>
public sealed class ClaimDailyGiftResultDto
{
    public bool Success { get; set; }
    public bool WasDuplicate { get; set; }
    public bool StreakReset { get; set; }
    public int ClaimedDay { get; set; }
    public DailyGiftRewardDto Reward { get; set; } = new();
    public PlayerProgressDto UpdatedProgress { get; set; } = new();
}

/// <summary>
/// Request to grant the one-time Rate Us reward.
/// </summary>
public sealed class ClaimRateUsRewardRequestDto
{
    public int Stars { get; set; }
}

/// <summary>
/// Result of granting the Rate Us reward.
/// </summary>
public sealed class ClaimRateUsRewardResultDto
{
    public bool Success { get; set; }
    public bool WasDuplicate { get; set; }
    public int CoinsGranted { get; set; }
    public PlayerProgressDto UpdatedProgress { get; set; } = new();
}

/// <summary>
/// Leaderboard scopes supported by backend.
/// </summary>
public static class LeaderboardScopes
{
    public const string World = "World";
    public const string Country = "Country";
}

/// <summary>
/// Request for leaderboard data.
/// </summary>
public sealed class GetLeaderboardRequestDto
{
    public string Scope { get; set; } = LeaderboardScopes.World;
    public int PageSize { get; set; } = 100;
    public int StartingPosition { get; set; } = 1;
}

/// <summary>
/// Single leaderboard row payload.
/// </summary>
public sealed class LeaderboardRowDto
{
    public string PlayerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int Stars { get; set; }
    public int Level { get; set; }
    public bool IsMe { get; set; }
}

/// <summary>
/// Leaderboard response payload.
/// </summary>
public sealed class GetLeaderboardResultDto
{
    public string Scope { get; set; } = LeaderboardScopes.World;
    public string CountryCode { get; set; } = string.Empty;
    public int StartingPosition { get; set; }
    public int PageSize { get; set; }
    public int EntryCount { get; set; }
    public List<LeaderboardRowDto> TopEntries { get; set; } = new();
    public LeaderboardRowDto? MeEntry { get; set; }
}
