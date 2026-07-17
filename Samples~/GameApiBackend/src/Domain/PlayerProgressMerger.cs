using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Serhat.Forge.CloudScript.Domain.DTOs;

namespace Serhat.Forge.CloudScript.Domain;

/// <summary>
/// Server-authoritative merge logic for player progress.
/// </summary>
public static class PlayerProgressMerger
{
    private const int DefaultLifeRegenSeconds = 1800;
    private const int DefaultMaxLives = 5;
    private const int DefaultStartingLives = 5;
    private const int DefaultStartingCoins = 100;
    private const int DefaultCoinCostPerLife = 100;
    private const int DefaultBoosterQuantity = 3;
    private const int DefaultSizeBoosterCoinPrice = 1500;
    private const int DefaultMagnetBoosterCoinPrice = 1500;
    private const int DefaultSpeedBoosterCoinPrice = 1500;
    private const int DefaultTimeBoosterCoinPrice = 1500;
    private const int DefaultCompassBoosterCoinPrice = 1500;
    private const int DefaultStartBoosterQuantity = 3;
    private const int DefaultStartXpBoosterCoinPrice = 2100;
    private const int DefaultStartPowerBoosterCoinPrice = 1500;
    private const int DefaultStartTimeBoosterCoinPrice = 1500;
    private const int DefaultLevelCoinReward = 10;
    private const int DefaultPiggyBankCoinsPerLevel = 40;
    private const int DefaultPiggyBankStartingCoins = 250;
    private const int DefaultPiggyBankMaxCoins = 6000;
    private const int DefaultPiggyBankDurationSeconds = 518400; // 6 days
    private const int DefaultFeatureUnlockLevel = 1;
    private const string PiggyBankFeatureId = "PiggyBank";
    private const int DefaultCrownEventMilestoneCount = 25;
    private const int DefaultCrownEventPhase1StartTimeBoosterCount = 1;
    private const int DefaultCrownEventPhase2StartXpBoosterCount = 2;
    private const int DefaultCrownEventPhase3InfiniteLivesMinutes = 15;
    private const int DefaultCrownEventPhase4Coins = 200;
    private const int DefaultCrownEventCycleDurationSeconds = 4200; // 1h 10m
    private static readonly int[] DefaultCrownEventRequiredCrownsPattern = { 1, 4, 7, 10 };
    private static readonly int DefaultCrownEventCrownsPerCycle = ResolveDefaultCrownEventCrownsPerCycle();
    private const int DefaultDailyGiftCycleLength = 7;
    private const int MaxDailyGiftCycleLength = 31;
    private static readonly DailyGiftRewardDto[] DefaultDailyGiftRewards =
    {
        new() { Day = 1, Coins = 50 },
        new() { Day = 2, Coins = 50 },
        new() { Day = 3, InfiniteLivesMinutes = 60 },
        new() { Day = 4, Boosters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "TimeBooster", 1 }, { "CompassBooster", 1 } } },
        new() { Day = 5, Coins = 100, Boosters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "MagnetBooster", 1 } } },
        new() { Day = 6, InfiniteLivesMinutes = 90, Boosters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "MagnetBooster", 2 } } },
        new() { Day = 7, Coins = 200, Boosters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "CompassBooster", 1 }, { "TimeBooster", 1 }, { "StartXpBooster", 1 } } }
    };
    private const int MaxTrackedPurchaseTransactions = 200;
    private const int MaxTrackedVerifiedPurchases = 300;

    private static readonly string[] DefaultBoosterTypes =
    {
        "SizeBooster",
        "MagnetBooster",
        "SpeedBooster",
        "TimeBooster",
        "CompassBooster",
        "StartXpBooster",
        "StartPowerBooster",
        "StartTimeBooster"
    };

    private static readonly int LifeRegenSeconds = ResolveLifeRegenSeconds();
    private static readonly int CoinCostPerLife = ResolveCoinCostPerLife();
    private static readonly int PiggyBankCoinsPerLevel = ResolvePiggyBankCoinsPerLevel();
    private static readonly int PiggyBankMaxCoins = ResolvePiggyBankMaxCoins();
    private static readonly int PiggyBankDurationSeconds = ResolvePiggyBankDurationSeconds();
    private static readonly int CrownEventCycleDurationSeconds = ResolveCrownEventCycleDurationSeconds();
    private static readonly CrownEventConfigDto DefaultCrownEventConfig = BuildDefaultCrownEventConfig();
    private static readonly BoosterOfferConfig[] BoosterOfferConfigs = ResolveBoosterOffers();
    private static readonly Dictionary<string, BoosterOfferConfig> BoosterOfferLookup =
        BuildBoosterOfferLookup(BoosterOfferConfigs);
    private static readonly StartBoosterOfferConfig[] StartBoosterOfferConfigs = ResolveStartBoosterOffers();
    private static readonly Dictionary<string, StartBoosterOfferConfig> StartBoosterOfferLookup =
        BuildStartBoosterOfferLookup(StartBoosterOfferConfigs);

    public static int CurrentCoinCostPerLife => CoinCostPerLife;

    public static CrownEventConfigDto GetCurrentCrownEventConfig(CrownEventConfigDto? crownEventConfig = null)
    {
        var effectiveConfig = ResolveCrownEventConfig(crownEventConfig);
        return CloneCrownEventConfig(effectiveConfig);
    }

    public static List<BoosterOfferDto> GetCurrentBoosterOffers()
    {
        var offers = new List<BoosterOfferDto>(BoosterOfferConfigs.Length);
        for (var i = 0; i < BoosterOfferConfigs.Length; i++)
        {
            var offer = BoosterOfferConfigs[i];
            offers.Add(new BoosterOfferDto
            {
                BoosterType = offer.BoosterType,
                CoinPrice = offer.CoinPrice,
                Quantity = offer.Quantity
            });
        }

        return offers;
    }

    public static DailyGiftConfigDto GetCurrentDailyGiftConfig(DailyGiftConfigDto? remoteOverride = null)
    {
        var effective = ResolveDailyGiftRewards(remoteOverride);
        var config = new DailyGiftConfigDto
        {
            Rewards = new List<DailyGiftRewardDto>(effective.Count)
        };

        for (var i = 0; i < effective.Count; i++)
        {
            config.Rewards.Add(CloneDailyGiftReward(effective[i]));
        }

        return config;
    }

    private static IReadOnlyList<DailyGiftRewardDto> ResolveDailyGiftRewards(DailyGiftConfigDto? remoteOverride)
    {
        if (remoteOverride?.Rewards != null && remoteOverride.Rewards.Count >= DailyGiftTitleDataParser.MinRewardCount)
        {
            var clampedCount = Math.Min(remoteOverride.Rewards.Count, MaxDailyGiftCycleLength);
            var cloned = new List<DailyGiftRewardDto>(clampedCount);
            for (var i = 0; i < clampedCount; i++)
            {
                var src = remoteOverride.Rewards[i];
                if (src == null)
                {
                    continue;
                }

                cloned.Add(CloneDailyGiftReward(src));
            }

            if (cloned.Count >= DailyGiftTitleDataParser.MinRewardCount)
            {
                return cloned;
            }
        }

        return DefaultDailyGiftRewards;
    }

    public static List<StartBoosterOfferDto> GetCurrentStartBoosterOffers()
    {
        var offers = new List<StartBoosterOfferDto>(StartBoosterOfferConfigs.Length);
        for (var i = 0; i < StartBoosterOfferConfigs.Length; i++)
        {
            var offer = StartBoosterOfferConfigs[i];
            offers.Add(new StartBoosterOfferDto
            {
                BoosterType = offer.BoosterType,
                CoinPrice = offer.CoinPrice,
                Quantity = offer.Quantity
            });
        }

        return offers;
    }

    /// <summary>
    /// Applies a completed level result to current progress.
    /// </summary>
    public static MergeResult ApplyLevelResult(
        PlayerProgressDto currentProgress,
        SubmitLevelResultRequestDto request,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        if (request.LevelId != currentProgress.CurrentLevel)
        {
            return MergeResult.Failure(
                ErrorCodes.InvalidLevel,
                $"Invalid level sequence. Expected: {currentProgress.CurrentLevel}, Received: {request.LevelId}");
        }

        var levelKey = request.LevelId.ToString(CultureInfo.InvariantCulture);

        if (currentProgress.Results.ContainsKey(levelKey))
        {
            return MergeResult.Failure(
                ErrorCodes.AlreadyCompleted,
                $"Level {request.LevelId} already completed.");
        }

        var newProgress = CloneProgress(currentProgress);
        var nowUtc = DateTime.UtcNow;
        ApplyLifeRegeneration(newProgress, nowUtc, gameplayBalance);
        NormalizePiggyBankState(newProgress, nowUtc, gameplayBalance);

        newProgress.Results[levelKey] = new LevelResultDto
        {
            Stars = request.Stars,
            TimeSec = request.TimeSec
        };

        var coinReward = GetServerCoinReward(request.LevelId);
        newProgress.Coins = ClampToInt((long)newProgress.Coins + coinReward);
        newProgress.TotalCoinsEarned = ClampToInt((long)newProgress.TotalCoinsEarned + coinReward);
        newProgress.Stars = ClampToInt((long)newProgress.Stars + request.Stars);
        newProgress.WinStreak = ClampToInt((long)newProgress.WinStreak + 1);
        ApplyPiggyBankLevelReward(newProgress, nowUtc, gameplayBalance);
        ApplyCrownEventProgress(
            newProgress,
            Math.Max(0, request.CrownsCollected),
            nowUtc,
            crownEventConfig);
        newProgress.CurrentLevel = request.LevelId + 1;
        newProgress.StateVersion++;
        newProgress.LastUpdatedUtc = nowUtc;

        return MergeResult.Success(newProgress);
    }

    /// <summary>
    /// Applies client-provided mutable state updates while preserving level progression history.
    /// Sync path is intentionally conservative: consumptions are accepted, gains are server-authoritative.
    /// </summary>
    public static MergeResult ApplyClientState(
        PlayerProgressDto currentProgress,
        PlayerProgressDto clientState,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        if (clientState == null)
        {
            return MergeResult.Failure(
                ErrorCodes.ValidationFailed,
                "Client state cannot be null.");
        }

        // Stale snapshots are ignored for mutable fields.
        // Accepting stale booster decreases can rollback newer server-side grants/purchases
        // when requests race (e.g. buy booster + consume another booster).
        var isStaleClientState =
            clientState.StateVersion > 0 &&
            clientState.StateVersion < currentProgress.StateVersion;

        var newProgress = CloneProgress(currentProgress);
        var now = DateTime.UtcNow;
        var changed = ApplyLifeRegeneration(newProgress, now, gameplayBalance);
        changed |= NormalizePiggyBankState(newProgress, now, gameplayBalance);
        var normalizedCrownEvent = NormalizeCrownEventState(newProgress.CrownEvent, now, crownEventConfig);
        if (!CrownEventStatesEqual(newProgress.CrownEvent, normalizedCrownEvent))
        {
            newProgress.CrownEvent = normalizedCrownEvent;
            changed = true;
        }

        if (!isStaleClientState)
        {
            // Allow client to request starting the piggy cycle only if no cycle exists yet.
            // This is needed so opening popup can start countdown even with zero coins.
            var requestedPiggyStartUtc = clientState.PiggyBankStartedUtc;
            if (IsPiggyBankUnlocked(newProgress, gameplayBalance) &&
                newProgress.PiggyBankStartedUtc == DateTime.MinValue &&
                requestedPiggyStartUtc != DateTime.MinValue)
            {
                var normalizedRequestedStartUtc = requestedPiggyStartUtc > now
                    ? now
                    : requestedPiggyStartUtc;
                var maxStartAgeSeconds = newProgress.PiggyBankDurationSeconds > 0
                    ? newProgress.PiggyBankDurationSeconds
                    : PiggyBankDurationSeconds;

                // Ignore stale starts from old client snapshots after expiry.
                if (normalizedRequestedStartUtc >= now.AddSeconds(-maxStartAgeSeconds))
                {
                    newProgress.PiggyBankStartedUtc = normalizedRequestedStartUtc;
                    changed = true;
                }
            }

            // MaxLives and infinite-lives flags are controlled by backend grants.
            newProgress.MaxLives = Math.Max(1, newProgress.MaxLives);

            // Lives can be reduced by client actions (fail/use) but not increased via sync.
            var requestedLives = Math.Clamp(clientState.Lives, 0, newProgress.MaxLives);
            if (requestedLives < newProgress.Lives)
            {
                newProgress.Lives = requestedLives;
                changed = true;

                if (newProgress.Lives < newProgress.MaxLives &&
                    !newProgress.HasInfiniteLives &&
                    newProgress.NextLifeTimeUtc == DateTime.MinValue)
                {
                    newProgress.NextLifeTimeUtc = now.AddSeconds(ResolveLifeRegenSeconds(gameplayBalance));
                    changed = true;
                }
            }

            if (newProgress.Lives >= newProgress.MaxLives && newProgress.NextLifeTimeUtc != DateTime.MinValue)
            {
                newProgress.NextLifeTimeUtc = DateTime.MinValue;
                changed = true;
            }

            // Coins can be spent by client, but gains must come from trusted server flows.
            var requestedCoins = Math.Max(0, clientState.Coins);
            if (requestedCoins < newProgress.Coins)
            {
                newProgress.Coins = requestedCoins;
                changed = true;
            }

            // Win streak can be reset by client (after fail), but increments come from level submit.
            var requestedWinStreak = Math.Max(0, clientState.WinStreak);
            if (requestedWinStreak < newProgress.WinStreak)
            {
                newProgress.WinStreak = requestedWinStreak;
                changed = true;
            }
        }

        if (!isStaleClientState)
        {
            // Booster counts can be consumed by client but not increased via sync.
            var mergedOwned = MergeDecreaseOnlyDictionary(
                newProgress.BoostersOwned,
                clientState.BoostersOwned,
                ensureDefaults: true);
            if (!DictionariesEqual(newProgress.BoostersOwned, mergedOwned))
            {
                newProgress.BoostersOwned = mergedOwned;
                changed = true;
            }

            var mergedFree = MergeDecreaseOnlyDictionary(
                newProgress.BoostersFree,
                clientState.BoostersFree,
                ensureDefaults: false);
            if (!DictionariesEqual(newProgress.BoostersFree, mergedFree))
            {
                newProgress.BoostersFree = mergedFree;
                changed = true;
            }
        }

        newProgress.TotalCoinsEarned = Math.Max(newProgress.TotalCoinsEarned, newProgress.Coins);
        newProgress.Stars = Math.Max(0, newProgress.Stars);

        if (changed)
        {
            newProgress.StateVersion++;
            newProgress.LastUpdatedUtc = now;
        }

        return MergeResult.Success(newProgress);
    }

    /// <summary>
    /// Buys lives with coins using the latest server-side rules.
    /// If LivesToBuy is not set, all missing lives are refilled.
    /// </summary>
    public static MergeResult BuyLivesWithCoins(
        PlayerProgressDto currentProgress,
        BuyLivesWithCoinsRequestDto? request,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        var nowUtc = DateTime.UtcNow;
        var (newProgress, changed) = PrepareMutableSnapshot(currentProgress, nowUtc, gameplayBalance, crownEventConfig);

        if (newProgress.HasInfiniteLives || newProgress.Lives >= newProgress.MaxLives)
        {
            return FinalizeMutation(newProgress, changed, nowUtc);
        }

        var missingLives = newProgress.MaxLives - newProgress.Lives;
        var requestedLives = request?.LivesToBuy is > 0
            ? Math.Min(request.LivesToBuy.Value, missingLives)
            : missingLives;

        if (requestedLives <= 0)
        {
            return FinalizeMutation(newProgress, changed, nowUtc);
        }

        var totalCost = (long)requestedLives * CoinCostPerLife;
        if (newProgress.Coins < totalCost)
        {
            return MergeResult.Failure(
                ErrorCodes.InsufficientFunds,
                $"Not enough coins to buy {requestedLives} lives.");
        }

        newProgress.Coins = ClampToInt((long)newProgress.Coins - totalCost);
        newProgress.Lives = Math.Min(newProgress.MaxLives, newProgress.Lives + requestedLives);
        newProgress.NextLifeTimeUtc = newProgress.Lives >= newProgress.MaxLives
            ? DateTime.MinValue
            : newProgress.NextLifeTimeUtc;

        return FinalizeMutation(newProgress, changed: true, nowUtc);
    }

    /// <summary>
    /// Grants one life for a rewarded ad if the player is not already full.
    /// </summary>
    public static MergeResult GrantAdRewardLife(
        PlayerProgressDto currentProgress,
        GrantAdRewardLifeRequestDto? request,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        _ = request;
        var nowUtc = DateTime.UtcNow;
        var (newProgress, changed) = PrepareMutableSnapshot(currentProgress, nowUtc, gameplayBalance, crownEventConfig);

        if (newProgress.HasInfiniteLives || newProgress.Lives >= newProgress.MaxLives)
        {
            return FinalizeMutation(newProgress, changed, nowUtc);
        }

        newProgress.Lives = Math.Min(newProgress.MaxLives, newProgress.Lives + 1);
        if (newProgress.Lives >= newProgress.MaxLives)
        {
            newProgress.NextLifeTimeUtc = DateTime.MinValue;
        }

        return FinalizeMutation(newProgress, changed: true, nowUtc);
    }

    /// <summary>
    /// Grants rewarded-ad coins.
    /// </summary>
    public static MergeResult GrantAdRewardCoins(
        PlayerProgressDto currentProgress,
        GrantAdRewardCoinsRequestDto? request,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null,
        int rewardCoins = 15)
    {
        _ = request;
        var nowUtc = DateTime.UtcNow;
        var (newProgress, changed) = PrepareMutableSnapshot(currentProgress, nowUtc, gameplayBalance, crownEventConfig);

        var normalizedRewardCoins = Math.Max(0, rewardCoins);
        if (normalizedRewardCoins <= 0)
        {
            return FinalizeMutation(newProgress, changed, nowUtc);
        }

        newProgress.Coins = ClampToInt((long)newProgress.Coins + normalizedRewardCoins);
        newProgress.TotalCoinsEarned = ClampToInt((long)newProgress.TotalCoinsEarned + normalizedRewardCoins);

        return FinalizeMutation(newProgress, changed: true, nowUtc);
    }

    /// <summary>
    /// Claims the current calendar day's daily-gift reward and advances the streak.
    /// Uses <paramref name="dailyGiftConfig"/> when provided (remote Title Data), else defaults.
    /// Returns the claimed day (1..cycleLength), whether streak reset, and a duplicate flag when already claimed today.
    /// </summary>
    public static DailyGiftClaimResult ClaimDailyGift(
        PlayerProgressDto currentProgress,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null,
        DailyGiftConfigDto? dailyGiftConfig = null)
    {
        var nowUtc = DateTime.UtcNow;
        var (newProgress, changed) = PrepareMutableSnapshot(currentProgress, nowUtc, gameplayBalance, crownEventConfig);

        var rewards = ResolveDailyGiftRewards(dailyGiftConfig);
        var cycleLength = rewards.Count;
        var previousStreakDay = Math.Clamp(newProgress.DailyGiftStreakDay, 0, cycleLength);
        var previousClaimUtc = newProgress.DailyGiftLastClaimedUtc;
        var previousClaimDate = previousClaimUtc == DateTime.MinValue ? DateTime.MinValue : previousClaimUtc.Date;
        var nowDate = nowUtc.Date;

        if (previousClaimUtc != DateTime.MinValue && previousClaimDate == nowDate)
        {
            var finalized = FinalizeMutation(newProgress, changed, nowUtc);
            return DailyGiftClaimResult.Duplicate(finalized.NewProgress!, previousStreakDay);
        }

        var isFirstClaim = previousClaimUtc == DateTime.MinValue;
        var isConsecutive = !isFirstClaim && previousClaimDate == nowDate.AddDays(-1);
        var streakReset = !isFirstClaim && !isConsecutive;

        int nextDay;
        if (!isConsecutive || previousStreakDay <= 0)
        {
            nextDay = 1;
        }
        else if (previousStreakDay >= cycleLength)
        {
            nextDay = 1;
        }
        else
        {
            nextDay = previousStreakDay + 1;
        }

        var reward = CloneDailyGiftReward(rewards[nextDay - 1]);
        ApplyDailyGiftReward(newProgress, reward, nowUtc);
        newProgress.DailyGiftStreakDay = nextDay;
        newProgress.DailyGiftLastClaimedUtc = nowUtc;

        var finalizedResult = FinalizeMutation(newProgress, changed: true, nowUtc);
        return DailyGiftClaimResult.Granted(finalizedResult.NewProgress!, nextDay, streakReset, reward);
    }

    private static DailyGiftRewardDto CloneDailyGiftReward(DailyGiftRewardDto source)
    {
        return new DailyGiftRewardDto
        {
            Day = source.Day,
            Coins = source.Coins,
            InfiniteLivesMinutes = source.InfiniteLivesMinutes,
            Boosters = source.Boosters == null
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(source.Boosters, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static void ApplyDailyGiftReward(PlayerProgressDto progress, DailyGiftRewardDto reward, DateTime nowUtc)
    {
        if (reward.Coins > 0)
        {
            progress.Coins = ClampToInt((long)progress.Coins + reward.Coins);
            progress.TotalCoinsEarned = ClampToInt((long)progress.TotalCoinsEarned + reward.Coins);
        }

        if (reward.InfiniteLivesMinutes > 0)
        {
            GrantInfiniteLivesMinutes(progress, reward.InfiniteLivesMinutes, nowUtc);
        }

        if (reward.Boosters == null || reward.Boosters.Count == 0)
        {
            return;
        }

        foreach (var (boosterType, count) in reward.Boosters)
        {
            AddBoosterToOwnedInventory(progress, boosterType, count);
        }
    }

    /// <summary>
    /// Buys a server-configured start booster offer with coins.
    /// </summary>
    public static MergeResult BuyStartBoosterWithCoins(
        PlayerProgressDto currentProgress,
        BuyStartBoosterWithCoinsRequestDto? request,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.BoosterType))
        {
            return MergeResult.Failure(
                ErrorCodes.InvalidRequest,
                "BoosterType is required.");
        }

        if (!TryGetStartBoosterOffer(request.BoosterType, out var offer))
        {
            return MergeResult.Failure(
                ErrorCodes.InvalidRequest,
                $"Unsupported start booster type: {request.BoosterType}");
        }

        var nowUtc = DateTime.UtcNow;
        var (newProgress, changed) = PrepareMutableSnapshot(currentProgress, nowUtc, gameplayBalance, crownEventConfig);

        if (newProgress.Coins < offer.CoinPrice)
        {
            return MergeResult.Failure(
                ErrorCodes.InsufficientFunds,
                $"Not enough coins to buy {offer.BoosterType}.");
        }

        newProgress.Coins = ClampToInt((long)newProgress.Coins - offer.CoinPrice);
        newProgress.BoostersOwned.TryGetValue(offer.InventoryKey, out var ownedCount);
        newProgress.BoostersOwned[offer.InventoryKey] = ClampToInt((long)ownedCount + offer.Quantity);

        return FinalizeMutation(newProgress, changed: true, nowUtc);
    }

    /// <summary>
    /// Buys a server-configured gameplay booster offer with coins.
    /// </summary>
    public static MergeResult BuyBoosterWithCoins(
        PlayerProgressDto currentProgress,
        BuyBoosterWithCoinsRequestDto? request,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.BoosterType))
        {
            return MergeResult.Failure(
                ErrorCodes.InvalidRequest,
                "BoosterType is required.");
        }

        if (!TryGetBoosterOffer(request.BoosterType, out var offer))
        {
            return MergeResult.Failure(
                ErrorCodes.InvalidRequest,
                $"Unsupported booster type: {request.BoosterType}");
        }

        var nowUtc = DateTime.UtcNow;
        var (newProgress, changed) = PrepareMutableSnapshot(currentProgress, nowUtc, gameplayBalance, crownEventConfig);

        if (newProgress.Coins < offer.CoinPrice)
        {
            return MergeResult.Failure(
                ErrorCodes.InsufficientFunds,
                $"Not enough coins to buy {offer.BoosterType}.");
        }

        newProgress.Coins = ClampToInt((long)newProgress.Coins - offer.CoinPrice);
        newProgress.BoostersOwned.TryGetValue(offer.InventoryKey, out var ownedCount);
        newProgress.BoostersOwned[offer.InventoryKey] = ClampToInt((long)ownedCount + offer.Quantity);

        return FinalizeMutation(newProgress, changed: true, nowUtc);
    }

    /// <summary>
    /// Creates default progress for new players.
    /// </summary>
    public static PlayerProgressDto CreateDefaultProgress(
        string playerId,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        var nowUtc = DateTime.UtcNow;
        var maxLives = ResolveMaxLives(gameplayBalance);
        var startingLives = ResolveStartingLives(gameplayBalance, maxLives);
        var startingCoins = ResolveStartingCoins(gameplayBalance);
        var piggyBankUnlockedAtStart = IsPiggyBankUnlocked(currentLevel: 1, gameplayBalance);
        var crownCycleDurationSeconds = ResolveCrownEventCycleDurationSeconds(crownEventConfig);

        var defaultOwned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var booster in DefaultBoosterTypes)
        {
            defaultOwned[booster] = 1;
        }

        return new PlayerProgressDto
        {
            SchemaVersion = 1,
            StateVersion = 1,
            PlayerId = playerId,
            CurrentLevel = 1,
            Lives = startingLives,
            MaxLives = maxLives,
            Coins = startingCoins,
            TotalCoinsEarned = startingCoins,
            Stars = 0,
            HasInfiniteLives = false,
            InfiniteLivesEndUtc = DateTime.MinValue,
            NextLifeTimeUtc = DateTime.MinValue,
            WinStreak = 0,
            HasRemovedAds = false,
            BoostersOwned = defaultOwned,
            BoostersFree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            VerifiedPurchases = new Dictionary<string, VerifiedPurchaseRecordDto>(StringComparer.OrdinalIgnoreCase),
            ActiveSubscription = null,
            ProcessedPurchaseTransactions = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase),
            ClaimedRewards = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase),
            PiggyBankCoins = piggyBankUnlockedAtStart ? DefaultPiggyBankStartingCoins : 0,
            PiggyBankStartedUtc = piggyBankUnlockedAtStart ? nowUtc : DateTime.MinValue,
            PiggyBankDurationSeconds = PiggyBankDurationSeconds,
            PiggyBankMaxCoins = PiggyBankMaxCoins,
            CrownEvent = CreateInitialCrownEventState(nowUtc, crownCycleDurationSeconds),
            DailyGiftStreakDay = 0,
            DailyGiftLastClaimedUtc = DateTime.MinValue,
            Results = new Dictionary<string, LevelResultDto>(StringComparer.Ordinal),
            LastUpdatedUtc = nowUtc
        };
    }

    /// <summary>
    /// Ensures missing fields from older records are backfilled with safe defaults.
    /// </summary>
    public static PlayerProgressDto NormalizeProgress(
        PlayerProgressDto progress,
        string playerId,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        var normalized = CloneProgress(progress);
        var defaultMaxLives = ResolveMaxLives(gameplayBalance);
        normalized.PlayerId = playerId;
        normalized.SchemaVersion = normalized.SchemaVersion <= 0 ? 1 : normalized.SchemaVersion;
        normalized.CurrentLevel = normalized.CurrentLevel <= 0 ? 1 : normalized.CurrentLevel;
        normalized.MaxLives = normalized.MaxLives <= 0 ? defaultMaxLives : normalized.MaxLives;
        normalized.Lives = Math.Clamp(normalized.Lives, 0, normalized.MaxLives);
        normalized.Coins = Math.Max(0, normalized.Coins);
        normalized.TotalCoinsEarned = Math.Max(normalized.Coins, normalized.TotalCoinsEarned);
        normalized.Stars = Math.Max(0, normalized.Stars);
        normalized.WinStreak = Math.Max(0, normalized.WinStreak);
        normalized.BoostersOwned = NormalizeBoosterDictionary(normalized.BoostersOwned, ensureDefaults: true);
        normalized.BoostersFree = NormalizeBoosterDictionary(normalized.BoostersFree, ensureDefaults: false);
        normalized.VerifiedPurchases = NormalizeVerifiedPurchases(normalized.VerifiedPurchases);
        normalized.ActiveSubscription = NormalizeSubscription(normalized.ActiveSubscription);
        normalized.ProcessedPurchaseTransactions = NormalizeTransactionDictionary(normalized.ProcessedPurchaseTransactions);
        normalized.ClaimedRewards = NormalizeTransactionDictionary(normalized.ClaimedRewards);
        normalized.DailyGiftStreakDay = Math.Clamp(normalized.DailyGiftStreakDay, 0, MaxDailyGiftCycleLength);
        if (normalized.DailyGiftLastClaimedUtc == default)
        {
            normalized.DailyGiftLastClaimedUtc = DateTime.MinValue;
        }
        var nowUtc = DateTime.UtcNow;
        normalized.CrownEvent = NormalizeCrownEventState(normalized.CrownEvent, nowUtc, crownEventConfig);
        normalized.Results ??= new Dictionary<string, LevelResultDto>(StringComparer.Ordinal);
        ApplyLifeRegeneration(normalized, nowUtc, gameplayBalance);
        NormalizePiggyBankState(normalized, nowUtc, gameplayBalance);
        normalized.LastUpdatedUtc = normalized.LastUpdatedUtc == default ? nowUtc : normalized.LastUpdatedUtc;
        return normalized;
    }

    /// <summary>
    /// Merges a candidate write with the latest persisted snapshot while preventing
    /// progression regressions caused by concurrent writes.
    /// </summary>
    public static PlayerProgressDto MergeForPersistence(
        PlayerProgressDto persisted,
        PlayerProgressDto candidate,
        string playerId,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        var latest = NormalizeProgress(persisted, playerId, gameplayBalance, crownEventConfig);
        var merged = NormalizeProgress(candidate, playerId, gameplayBalance, crownEventConfig);

        merged.Results = MergeLevelResults(latest.Results, merged.Results);
        merged.CurrentLevel = Math.Max(merged.CurrentLevel, latest.CurrentLevel);

        var highestCompleted = 0;
        foreach (var key in merged.Results.Keys)
        {
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var levelId) &&
                levelId > highestCompleted)
            {
                highestCompleted = levelId;
            }
        }

        if (highestCompleted > 0)
        {
            merged.CurrentLevel = Math.Max(merged.CurrentLevel, highestCompleted + 1);
        }

        merged.VerifiedPurchases = MergeVerifiedPurchases(latest.VerifiedPurchases, merged.VerifiedPurchases);
        merged.ProcessedPurchaseTransactions = MergeTransactions(
            latest.ProcessedPurchaseTransactions,
            merged.ProcessedPurchaseTransactions);
        merged.ClaimedRewards = MergeTransactions(
            latest.ClaimedRewards,
            merged.ClaimedRewards);
        merged.ActiveSubscription = ChooseMostRecentSubscription(latest.ActiveSubscription, merged.ActiveSubscription);
        merged.CrownEvent = MergeCrownEventState(latest.CrownEvent, merged.CrownEvent, DateTime.UtcNow, crownEventConfig);
        merged.StateVersion = Math.Max(merged.StateVersion, latest.StateVersion);
        merged.HasRemovedAds = latest.HasRemovedAds || merged.HasRemovedAds;

        return merged;
    }

    private static (PlayerProgressDto Progress, bool Changed) PrepareMutableSnapshot(
        PlayerProgressDto currentProgress,
        DateTime nowUtc,
        GameplayBalanceConfigDto? gameplayBalance = null,
        CrownEventConfigDto? crownEventConfig = null)
    {
        var newProgress = CloneProgress(currentProgress);
        var changed = ApplyLifeRegeneration(newProgress, nowUtc, gameplayBalance);
        changed |= NormalizePiggyBankState(newProgress, nowUtc, gameplayBalance);
        var normalizedCrownEvent = NormalizeCrownEventState(newProgress.CrownEvent, nowUtc, crownEventConfig);
        if (!CrownEventStatesEqual(newProgress.CrownEvent, normalizedCrownEvent))
        {
            changed = true;
        }

        newProgress.CrownEvent = normalizedCrownEvent;
        newProgress.MaxLives = Math.Max(1, newProgress.MaxLives);
        newProgress.Lives = Math.Clamp(newProgress.Lives, 0, newProgress.MaxLives);
        newProgress.Coins = Math.Max(0, newProgress.Coins);
        newProgress.TotalCoinsEarned = Math.Max(newProgress.TotalCoinsEarned, newProgress.Coins);
        return (newProgress, changed);
    }

    private static MergeResult FinalizeMutation(
        PlayerProgressDto progress,
        bool changed,
        DateTime nowUtc)
    {
        if (changed)
        {
            progress.StateVersion++;
            progress.LastUpdatedUtc = nowUtc;
        }

        return MergeResult.Success(progress);
    }

    private static CrownEventConfigDto BuildDefaultCrownEventConfig()
    {
        var milestones = new List<CrownEventMilestoneDto>(DefaultCrownEventMilestoneCount);
        var patternLength = DefaultCrownEventRequiredCrownsPattern.Length;

        for (var milestoneIndex = 1; milestoneIndex <= DefaultCrownEventMilestoneCount; milestoneIndex++)
        {
            var phase = (milestoneIndex - 1) % patternLength;
            var milestone = new CrownEventMilestoneDto
            {
                MilestoneIndex = milestoneIndex,
                RequiredCrowns = DefaultCrownEventRequiredCrownsPattern[phase]
            };

            switch (phase)
            {
                case 0:
                    milestone.Boosters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["StartTimeBooster"] = DefaultCrownEventPhase1StartTimeBoosterCount
                    };
                    break;
                case 1:
                    milestone.Boosters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["StartXpBooster"] = DefaultCrownEventPhase2StartXpBoosterCount
                    };
                    break;
                case 2:
                    milestone.InfiniteLivesMinutes = DefaultCrownEventPhase3InfiniteLivesMinutes;
                    break;
                case 3:
                    milestone.Coins = DefaultCrownEventPhase4Coins;
                    break;
            }

            milestones.Add(milestone);
        }

        return new CrownEventConfigDto
        {
            // Per-milestone reset flow uses individual milestone thresholds.
            // CrownsPerCycle stays as aggregate informational value for UI.
            CrownsPerCycle = ResolveCrownsPerCycleFromMilestones(milestones),
            CycleDurationSeconds = CrownEventCycleDurationSeconds,
            Milestones = milestones
        };
    }

    private static int ResolveDefaultCrownEventCrownsPerCycle()
    {
        if (DefaultCrownEventRequiredCrownsPattern.Length == 0 || DefaultCrownEventMilestoneCount <= 0)
        {
            return 1;
        }

        var total = 0;
        for (var i = 0; i < DefaultCrownEventMilestoneCount; i++)
        {
            total += Math.Max(1, DefaultCrownEventRequiredCrownsPattern[i % DefaultCrownEventRequiredCrownsPattern.Length]);
        }

        return Math.Max(1, total);
    }

    private static CrownEventStateDto CreateInitialCrownEventState(DateTime nowUtc, int cycleDurationSeconds)
    {
        var normalizedCycleDurationSeconds = Math.Max(0, cycleDurationSeconds);
        return new CrownEventStateDto
        {
            CycleIndex = 0,
            CrownsInCycle = 0,
            ClaimedMilestones = new List<int>(),
            CycleStartedUtc = normalizedCycleDurationSeconds > 0 ? nowUtc : DateTime.MinValue,
            CycleDurationSeconds = normalizedCycleDurationSeconds,
            LastUpdatedUtc = nowUtc
        };
    }

    private static CrownEventConfigDto CloneCrownEventConfig(CrownEventConfigDto source)
    {
        var clone = new CrownEventConfigDto
        {
            Milestones = new List<CrownEventMilestoneDto>()
        };

        var milestones = (source.Milestones ?? new List<CrownEventMilestoneDto>())
            .Where(m => m != null && m.MilestoneIndex > 0 && m.RequiredCrowns > 0)
            .OrderBy(m => m.MilestoneIndex)
            .ToList();

        for (var i = 0; i < milestones.Count; i++)
        {
            var milestone = milestones[i];

            clone.Milestones.Add(new CrownEventMilestoneDto
            {
                MilestoneIndex = milestone.MilestoneIndex,
                RequiredCrowns = milestone.RequiredCrowns,
                Coins = milestone.Coins,
                InfiniteLivesMinutes = milestone.InfiniteLivesMinutes,
                Boosters = new Dictionary<string, int>(
                    milestone.Boosters ?? new Dictionary<string, int>(),
                    StringComparer.OrdinalIgnoreCase)
            });
        }

        var computedCrownsPerCycle = ResolveCrownsPerCycleFromMilestones(clone.Milestones);
        clone.CrownsPerCycle = computedCrownsPerCycle > 0
            ? computedCrownsPerCycle
            : (source.CrownsPerCycle > 0 ? source.CrownsPerCycle : DefaultCrownEventCrownsPerCycle);
        clone.CycleDurationSeconds = source.CycleDurationSeconds > 0
            ? source.CycleDurationSeconds
            : CrownEventCycleDurationSeconds;
        return clone;
    }

    private static CrownEventStateDto CloneCrownEventState(CrownEventStateDto? source)
    {
        if (source == null)
        {
            return new CrownEventStateDto();
        }

        return new CrownEventStateDto
        {
            CycleIndex = source.CycleIndex,
            CrownsInCycle = source.CrownsInCycle,
            ClaimedMilestones = new List<int>(source.ClaimedMilestones ?? new List<int>()),
            CycleStartedUtc = source.CycleStartedUtc,
            CycleDurationSeconds = source.CycleDurationSeconds,
            LastUpdatedUtc = source.LastUpdatedUtc
        };
    }

    private static CrownEventStateDto NormalizeCrownEventState(
        CrownEventStateDto? source,
        DateTime nowUtc,
        CrownEventConfigDto? crownEventConfig = null)
    {
        var normalized = CloneCrownEventState(source);
        var milestones = GetOrderedCrownEventMilestones(crownEventConfig);
        var knownMilestoneIndices = new HashSet<int>(milestones.Select(m => m.MilestoneIndex));

        normalized.CycleIndex = Math.Max(0, normalized.CycleIndex);
        normalized.ClaimedMilestones ??= new List<int>();

        var filteredMilestones = new List<int>(normalized.ClaimedMilestones.Count);
        for (var i = 0; i < normalized.ClaimedMilestones.Count; i++)
        {
            var milestoneIndex = normalized.ClaimedMilestones[i];
            if (milestoneIndex <= 0 || filteredMilestones.Contains(milestoneIndex))
            {
                continue;
            }

            if (!knownMilestoneIndices.Contains(milestoneIndex))
            {
                continue;
            }

            filteredMilestones.Add(milestoneIndex);
        }

        filteredMilestones.Sort();
        normalized.ClaimedMilestones = filteredMilestones;

        normalized.CycleDurationSeconds = ResolveCrownEventCycleDurationSeconds(crownEventConfig);
        if (normalized.CycleDurationSeconds > 0)
        {
            if (normalized.CycleStartedUtc == DateTime.MinValue || normalized.CycleStartedUtc > nowUtc)
            {
                normalized.CycleStartedUtc = nowUtc;
            }

            if (nowUtc >= normalized.CycleStartedUtc.AddSeconds(normalized.CycleDurationSeconds))
            {
                ResetCrownEventCycleProgress(
                    normalized,
                    nowUtc,
                    normalized.CycleDurationSeconds,
                    incrementCycleIndex: false);
            }
        }
        else if (normalized.CycleStartedUtc != DateTime.MinValue)
        {
            normalized.CycleStartedUtc = DateTime.MinValue;
        }

        var claimedSet = new HashSet<int>(normalized.ClaimedMilestones);
        if (TryGetNextCrownMilestone(claimedSet, milestones, out var nextMilestone))
        {
            normalized.CrownsInCycle = Math.Clamp(
                normalized.CrownsInCycle,
                0,
                Math.Max(1, nextMilestone.RequiredCrowns));
        }
        else
        {
            normalized.CrownsInCycle = 0;
        }

        normalized.LastUpdatedUtc = normalized.LastUpdatedUtc == default ? nowUtc : normalized.LastUpdatedUtc;

        return normalized;
    }

    private static List<CrownEventMilestoneDto> GetOrderedCrownEventMilestones(CrownEventConfigDto? crownEventConfig = null)
    {
        var effectiveConfig = ResolveCrownEventConfig(crownEventConfig);
        return (effectiveConfig.Milestones ?? new List<CrownEventMilestoneDto>())
            .Where(m => m != null && m.MilestoneIndex > 0 && m.RequiredCrowns > 0)
            .OrderBy(m => m.MilestoneIndex)
            .ToList();
    }

    private static CrownEventConfigDto ResolveCrownEventConfig(CrownEventConfigDto? crownEventConfig)
    {
        if (crownEventConfig == null || crownEventConfig.Milestones == null || crownEventConfig.Milestones.Count == 0)
        {
            return DefaultCrownEventConfig;
        }

        return crownEventConfig;
    }

    private static int ResolveCrownEventCycleDurationSeconds(CrownEventConfigDto? crownEventConfig)
    {
        var resolvedConfig = ResolveCrownEventConfig(crownEventConfig);
        return resolvedConfig.CycleDurationSeconds > 0
            ? resolvedConfig.CycleDurationSeconds
            : CrownEventCycleDurationSeconds;
    }

    private static bool TryGetNextCrownMilestone(
        HashSet<int> claimedMilestones,
        IReadOnlyList<CrownEventMilestoneDto> milestones,
        out CrownEventMilestoneDto milestone)
    {
        for (var i = 0; i < milestones.Count; i++)
        {
            var candidate = milestones[i];
            if (candidate.RequiredCrowns <= 0)
            {
                continue;
            }

            if (!claimedMilestones.Contains(candidate.MilestoneIndex))
            {
                milestone = candidate;
                return true;
            }
        }

        milestone = null!;
        return false;
    }

    private static int ResolveCrownsPerCycleFromMilestones(IEnumerable<CrownEventMilestoneDto>? milestones)
    {
        if (milestones == null)
        {
            return DefaultCrownEventCrownsPerCycle;
        }

        long total = 0;
        foreach (var milestone in milestones)
        {
            if (milestone == null || milestone.RequiredCrowns <= 0)
            {
                continue;
            }

            total += milestone.RequiredCrowns;
        }

        return total > 0 ? ClampToInt(total) : DefaultCrownEventCrownsPerCycle;
    }

    private static CrownEventStateDto MergeCrownEventState(
        CrownEventStateDto? persisted,
        CrownEventStateDto? candidate,
        DateTime nowUtc,
        CrownEventConfigDto? crownEventConfig = null)
    {
        var latest = NormalizeCrownEventState(persisted, nowUtc, crownEventConfig);
        var merged = NormalizeCrownEventState(candidate, nowUtc, crownEventConfig);

        if (merged.CycleIndex != latest.CycleIndex)
        {
            return merged.CycleIndex > latest.CycleIndex ? merged : latest;
        }

        if (merged.CrownsInCycle != latest.CrownsInCycle)
        {
            // In staged milestone flow, claimed milestones indicate progression stage.
            // Compare milestone stage before raw crown progress to avoid regressions
            // where a freshly-claimed milestone resets crowns to zero.
            if (merged.ClaimedMilestones.Count != latest.ClaimedMilestones.Count)
            {
                return merged.ClaimedMilestones.Count > latest.ClaimedMilestones.Count ? merged : latest;
            }

            for (var i = 0; i < merged.ClaimedMilestones.Count; i++)
            {
                if (merged.ClaimedMilestones[i] == latest.ClaimedMilestones[i])
                {
                    continue;
                }

                return merged.ClaimedMilestones[i] > latest.ClaimedMilestones[i] ? merged : latest;
            }

            return merged.CrownsInCycle > latest.CrownsInCycle ? merged : latest;
        }

        if (merged.ClaimedMilestones.Count != latest.ClaimedMilestones.Count)
        {
            return merged.ClaimedMilestones.Count > latest.ClaimedMilestones.Count ? merged : latest;
        }

        for (var i = 0; i < merged.ClaimedMilestones.Count; i++)
        {
            if (merged.ClaimedMilestones[i] == latest.ClaimedMilestones[i])
            {
                continue;
            }

            return merged.ClaimedMilestones[i] > latest.ClaimedMilestones[i] ? merged : latest;
        }

        return merged.LastUpdatedUtc >= latest.LastUpdatedUtc ? merged : latest;
    }

    private static bool CrownEventStatesEqual(CrownEventStateDto? left, CrownEventStateDto? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        if (left.CycleIndex != right.CycleIndex ||
            left.CrownsInCycle != right.CrownsInCycle ||
            left.CycleStartedUtc != right.CycleStartedUtc ||
            left.CycleDurationSeconds != right.CycleDurationSeconds ||
            left.LastUpdatedUtc != right.LastUpdatedUtc)
        {
            return false;
        }

        var leftMilestones = left.ClaimedMilestones ?? new List<int>();
        var rightMilestones = right.ClaimedMilestones ?? new List<int>();
        if (leftMilestones.Count != rightMilestones.Count)
        {
            return false;
        }

        for (var i = 0; i < leftMilestones.Count; i++)
        {
            if (leftMilestones[i] != rightMilestones[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void ApplyCrownEventProgress(
        PlayerProgressDto progress,
        int crownsCollected,
        DateTime nowUtc,
        CrownEventConfigDto? crownEventConfig = null)
    {
        progress.CrownEvent = NormalizeCrownEventState(progress.CrownEvent, nowUtc, crownEventConfig);
        if (crownsCollected <= 0)
        {
            return;
        }

        var milestones = GetOrderedCrownEventMilestones(crownEventConfig);
        if (milestones.Count == 0)
        {
            return;
        }

        var cycleDurationSeconds = ResolveCrownEventCycleDurationSeconds(crownEventConfig);
        var remaining = crownsCollected;
        var claimedMilestones = new HashSet<int>(progress.CrownEvent.ClaimedMilestones);

        while (remaining > 0)
        {
            if (!TryGetNextCrownMilestone(claimedMilestones, milestones, out var nextMilestone))
            {
                CompleteCrownEventCycle(progress.CrownEvent, nowUtc, cycleDurationSeconds);
                claimedMilestones.Clear();
                if (!TryGetNextCrownMilestone(claimedMilestones, milestones, out nextMilestone))
                {
                    break;
                }
            }

            var requiredCrownsForMilestone = Math.Max(1, nextMilestone.RequiredCrowns);
            var crownsNeeded = requiredCrownsForMilestone - progress.CrownEvent.CrownsInCycle;
            if (crownsNeeded <= 0)
            {
                ClaimCrownEventMilestone(
                    progress,
                    progress.CrownEvent,
                    claimedMilestones,
                    milestones,
                    nextMilestone,
                    nowUtc,
                    cycleDurationSeconds);
                continue;
            }

            var crownsToAdd = Math.Min(remaining, crownsNeeded);
            if (crownsToAdd <= 0)
            {
                break;
            }

            progress.CrownEvent.CrownsInCycle += crownsToAdd;
            progress.CrownEvent.LastUpdatedUtc = nowUtc;
            remaining -= crownsToAdd;

            if (progress.CrownEvent.CrownsInCycle >= requiredCrownsForMilestone)
            {
                ClaimCrownEventMilestone(
                    progress,
                    progress.CrownEvent,
                    claimedMilestones,
                    milestones,
                    nextMilestone,
                    nowUtc,
                    cycleDurationSeconds);
            }
        }
    }

    private static void ClaimCrownEventMilestone(
        PlayerProgressDto progress,
        CrownEventStateDto crownEventState,
        HashSet<int> claimedMilestones,
        IReadOnlyList<CrownEventMilestoneDto> milestones,
        CrownEventMilestoneDto milestone,
        DateTime nowUtc,
        int cycleDurationSeconds)
    {
        if (milestone == null || milestone.RequiredCrowns <= 0)
        {
            return;
        }

        if (claimedMilestones.Contains(milestone.MilestoneIndex))
        {
            return;
        }

        ApplyCrownEventMilestoneReward(progress, milestone, nowUtc);
        claimedMilestones.Add(milestone.MilestoneIndex);
        crownEventState.ClaimedMilestones = claimedMilestones.OrderBy(x => x).ToList();
        crownEventState.CrownsInCycle = 0;
        crownEventState.LastUpdatedUtc = nowUtc;

        if (!TryGetNextCrownMilestone(claimedMilestones, milestones, out _))
        {
            CompleteCrownEventCycle(crownEventState, nowUtc, cycleDurationSeconds);
            claimedMilestones.Clear();
        }
    }

    private static void ApplyCrownEventMilestoneReward(
        PlayerProgressDto progress,
        CrownEventMilestoneDto milestone,
        DateTime nowUtc)
    {
        if (milestone.Coins > 0)
        {
            progress.Coins = ClampToInt((long)progress.Coins + milestone.Coins);
            progress.TotalCoinsEarned = ClampToInt((long)progress.TotalCoinsEarned + milestone.Coins);
        }

        if (milestone.InfiniteLivesMinutes > 0)
        {
            GrantInfiniteLivesMinutes(progress, milestone.InfiniteLivesMinutes, nowUtc);
        }

        if (milestone.Boosters == null || milestone.Boosters.Count == 0)
        {
            return;
        }

        foreach (var (boosterType, count) in milestone.Boosters)
        {
            AddBoosterToOwnedInventory(progress, boosterType, count);
        }
    }

    private static void CompleteCrownEventCycle(
        CrownEventStateDto crownEventState,
        DateTime nowUtc,
        int cycleDurationSeconds)
    {
        ResetCrownEventCycleProgress(crownEventState, nowUtc, cycleDurationSeconds, incrementCycleIndex: true);
    }

    private static void ResetCrownEventCycleProgress(
        CrownEventStateDto crownEventState,
        DateTime nowUtc,
        int cycleDurationSeconds,
        bool incrementCycleIndex)
    {
        if (incrementCycleIndex)
        {
            crownEventState.CycleIndex = ClampToInt((long)crownEventState.CycleIndex + 1);
        }
        else
        {
            crownEventState.CycleIndex = Math.Max(0, crownEventState.CycleIndex);
        }

        crownEventState.CrownsInCycle = 0;
        crownEventState.ClaimedMilestones.Clear();
        crownEventState.CycleDurationSeconds = Math.Max(0, cycleDurationSeconds);
        crownEventState.CycleStartedUtc = crownEventState.CycleDurationSeconds > 0
            ? nowUtc
            : DateTime.MinValue;
        crownEventState.LastUpdatedUtc = nowUtc;
    }

    private static void GrantInfiniteLivesMinutes(PlayerProgressDto progress, int minutes, DateTime nowUtc)
    {
        if (minutes <= 0)
        {
            return;
        }

        var additionalSeconds = (long)minutes * 60L;
        if (additionalSeconds <= 0)
        {
            return;
        }

        progress.HasInfiniteLives = true;
        progress.NextLifeTimeUtc = DateTime.MinValue;

        var baseEnd = progress.InfiniteLivesEndUtc > nowUtc ? progress.InfiniteLivesEndUtc : nowUtc;
        progress.InfiniteLivesEndUtc = baseEnd.AddSeconds(additionalSeconds);
    }

    private static void AddBoosterToOwnedInventory(PlayerProgressDto progress, string boosterType, int count)
    {
        if (string.IsNullOrWhiteSpace(boosterType) || count <= 0)
        {
            return;
        }

        progress.BoostersOwned ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        progress.BoostersOwned.TryGetValue(boosterType, out var currentCount);
        progress.BoostersOwned[boosterType] = ClampToInt((long)currentCount + count);
    }

    private static PlayerProgressDto CloneProgress(PlayerProgressDto progress)
    {
        return new PlayerProgressDto
        {
            SchemaVersion = progress.SchemaVersion,
            StateVersion = progress.StateVersion,
            PlayerId = progress.PlayerId,
            CurrentLevel = progress.CurrentLevel,
            Lives = progress.Lives,
            MaxLives = progress.MaxLives,
            Coins = progress.Coins,
            TotalCoinsEarned = progress.TotalCoinsEarned,
            Stars = progress.Stars,
            HasInfiniteLives = progress.HasInfiniteLives,
            InfiniteLivesEndUtc = progress.InfiniteLivesEndUtc,
            NextLifeTimeUtc = progress.NextLifeTimeUtc,
            WinStreak = progress.WinStreak,
            HasRemovedAds = progress.HasRemovedAds,
            BoostersOwned = new Dictionary<string, int>(progress.BoostersOwned ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase),
            BoostersFree = new Dictionary<string, int>(progress.BoostersFree ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase),
            VerifiedPurchases = NormalizeVerifiedPurchases(progress.VerifiedPurchases),
            ActiveSubscription = CloneSubscription(progress.ActiveSubscription),
            ProcessedPurchaseTransactions = NormalizeTransactionDictionary(progress.ProcessedPurchaseTransactions),
            ClaimedRewards = NormalizeTransactionDictionary(progress.ClaimedRewards),
            PiggyBankCoins = progress.PiggyBankCoins,
            PiggyBankStartedUtc = progress.PiggyBankStartedUtc,
            PiggyBankDurationSeconds = progress.PiggyBankDurationSeconds,
            PiggyBankMaxCoins = progress.PiggyBankMaxCoins,
            CrownEvent = CloneCrownEventState(progress.CrownEvent),
            DailyGiftStreakDay = progress.DailyGiftStreakDay,
            DailyGiftLastClaimedUtc = progress.DailyGiftLastClaimedUtc,
            Results = new Dictionary<string, LevelResultDto>(
                progress.Results ?? new Dictionary<string, LevelResultDto>(),
                StringComparer.Ordinal),
            LastUpdatedUtc = progress.LastUpdatedUtc
        };
    }

    private static Dictionary<string, LevelResultDto> MergeLevelResults(
        Dictionary<string, LevelResultDto>? persisted,
        Dictionary<string, LevelResultDto>? candidate)
    {
        var merged = new Dictionary<string, LevelResultDto>(StringComparer.Ordinal);

        if (persisted != null)
        {
            foreach (var (key, value) in persisted)
            {
                if (string.IsNullOrWhiteSpace(key) || value == null)
                {
                    continue;
                }

                merged[key] = new LevelResultDto
                {
                    Stars = value.Stars,
                    TimeSec = value.TimeSec
                };
            }
        }

        if (candidate != null)
        {
            foreach (var (key, value) in candidate)
            {
                if (string.IsNullOrWhiteSpace(key) || value == null)
                {
                    continue;
                }

                if (!merged.TryGetValue(key, out var existing))
                {
                    merged[key] = new LevelResultDto
                    {
                        Stars = value.Stars,
                        TimeSec = value.TimeSec
                    };
                    continue;
                }

                // Prefer higher stars; for same stars keep better (lower) time.
                if (value.Stars > existing.Stars ||
                    (value.Stars == existing.Stars &&
                     value.TimeSec > 0f &&
                     (existing.TimeSec <= 0f || value.TimeSec < existing.TimeSec)))
                {
                    merged[key] = new LevelResultDto
                    {
                        Stars = value.Stars,
                        TimeSec = value.TimeSec
                    };
                }
            }
        }

        return merged;
    }

    private static Dictionary<string, VerifiedPurchaseRecordDto> MergeVerifiedPurchases(
        Dictionary<string, VerifiedPurchaseRecordDto>? persisted,
        Dictionary<string, VerifiedPurchaseRecordDto>? candidate)
    {
        var merged = new Dictionary<string, VerifiedPurchaseRecordDto>(StringComparer.OrdinalIgnoreCase);

        void Upsert(Dictionary<string, VerifiedPurchaseRecordDto>? source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var (key, value) in source)
            {
                if (string.IsNullOrWhiteSpace(key) || value == null)
                {
                    continue;
                }

                if (!merged.TryGetValue(key, out var existing) ||
                    value.VerifiedAtUtc > existing.VerifiedAtUtc)
                {
                    merged[key] = new VerifiedPurchaseRecordDto
                    {
                        ProductId = value.ProductId ?? string.Empty,
                        TransactionId = string.IsNullOrWhiteSpace(value.TransactionId) ? key : value.TransactionId,
                        Platform = value.Platform ?? string.Empty,
                        ProductType = value.ProductType,
                        TierKey = value.TierKey ?? string.Empty,
                        WasRestored = value.WasRestored,
                        GrantedItemId = value.GrantedItemId ?? string.Empty,
                        VerifiedAtUtc = value.VerifiedAtUtc == default ? DateTime.UtcNow : value.VerifiedAtUtc
                    };
                }
            }
        }

        Upsert(persisted);
        Upsert(candidate);
        TrimOldestVerifiedPurchases(merged, MaxTrackedVerifiedPurchases);
        return merged;
    }

    private static Dictionary<string, DateTime> MergeTransactions(
        Dictionary<string, DateTime>? persisted,
        Dictionary<string, DateTime>? candidate)
    {
        var merged = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        void Upsert(Dictionary<string, DateTime>? source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var (key, value) in source)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var normalized = value == default ? DateTime.UtcNow : value;
                if (!merged.TryGetValue(key, out var existing) || normalized > existing)
                {
                    merged[key] = normalized;
                }
            }
        }

        Upsert(persisted);
        Upsert(candidate);
        TrimOldestTransactions(merged, MaxTrackedPurchaseTransactions);
        return merged;
    }

    private static SubscriptionDto? ChooseMostRecentSubscription(
        SubscriptionDto? persisted,
        SubscriptionDto? candidate)
    {
        if (persisted == null)
        {
            return candidate;
        }

        if (candidate == null)
        {
            return persisted;
        }

        return candidate.PeriodEndUtc >= persisted.PeriodEndUtc
            ? candidate
            : persisted;
    }

    private static Dictionary<string, int> NormalizeBoosterDictionary(
        Dictionary<string, int>? source,
        bool ensureDefaults)
    {
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (source != null)
        {
            foreach (var (key, value) in source)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                normalized[key] = Math.Max(0, value);
            }
        }

        if (ensureDefaults)
        {
            foreach (var booster in DefaultBoosterTypes)
            {
                if (!normalized.ContainsKey(booster))
                {
                    normalized[booster] = 1;
                }
            }
        }

        return normalized;
    }

    private static Dictionary<string, int> MergeDecreaseOnlyDictionary(
        Dictionary<string, int>? current,
        Dictionary<string, int>? requested,
        bool ensureDefaults)
    {
        var merged = NormalizeBoosterDictionary(current, ensureDefaults);
        if (requested == null)
        {
            return merged;
        }

        foreach (var (key, value) in requested)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!merged.TryGetValue(key, out var existing))
            {
                continue;
            }

            var requestedValue = Math.Max(0, value);
            if (requestedValue < existing)
            {
                merged[key] = requestedValue;
            }
        }

        return merged;
    }

    private static Dictionary<string, DateTime> NormalizeTransactionDictionary(
        Dictionary<string, DateTime>? source)
    {
        var normalized = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
        {
            return normalized;
        }

        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            normalized[key] = value == default ? DateTime.UtcNow : value;
        }

        TrimOldestTransactions(normalized, MaxTrackedPurchaseTransactions);
        return normalized;
    }

    private static Dictionary<string, VerifiedPurchaseRecordDto> NormalizeVerifiedPurchases(
        Dictionary<string, VerifiedPurchaseRecordDto>? source)
    {
        var normalized = new Dictionary<string, VerifiedPurchaseRecordDto>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
        {
            return normalized;
        }

        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key) || value == null)
            {
                continue;
            }

            var transactionId = string.IsNullOrWhiteSpace(value.TransactionId)
                ? key
                : value.TransactionId.Trim();

            normalized[transactionId] = new VerifiedPurchaseRecordDto
            {
                ProductId = value.ProductId ?? string.Empty,
                TransactionId = transactionId,
                Platform = value.Platform ?? string.Empty,
                ProductType = value.ProductType,
                TierKey = value.TierKey ?? string.Empty,
                WasRestored = value.WasRestored,
                GrantedItemId = value.GrantedItemId ?? string.Empty,
                VerifiedAtUtc = value.VerifiedAtUtc == default ? DateTime.UtcNow : value.VerifiedAtUtc
            };
        }

        TrimOldestVerifiedPurchases(normalized, MaxTrackedVerifiedPurchases);
        return normalized;
    }

    private static SubscriptionDto? NormalizeSubscription(SubscriptionDto? source)
    {
        if (source == null)
        {
            return null;
        }

        var normalized = CloneSubscription(source);
        normalized!.ProductId = normalized.ProductId ?? string.Empty;
        normalized.TierKey = normalized.TierKey ?? string.Empty;
        normalized.Platform = normalized.Platform ?? string.Empty;
        normalized.GrantedItemId ??= string.Empty;

        if (normalized.PeriodStartUtc == default)
        {
            normalized.PeriodStartUtc = DateTime.UtcNow;
        }

        if (normalized.OriginalPurchaseDateUtc == default)
        {
            normalized.OriginalPurchaseDateUtc = normalized.PeriodStartUtc;
        }

        return normalized;
    }

    private static SubscriptionDto? CloneSubscription(SubscriptionDto? source)
    {
        if (source == null)
        {
            return null;
        }

        return new SubscriptionDto
        {
            ProductId = source.ProductId,
            TierKey = source.TierKey,
            Status = source.Status,
            AutoRenew = source.AutoRenew,
            PeriodStartUtc = source.PeriodStartUtc,
            PeriodEndUtc = source.PeriodEndUtc,
            OriginalPurchaseDateUtc = source.OriginalPurchaseDateUtc,
            Platform = source.Platform,
            GrantedItemId = source.GrantedItemId,
            GracePeriodDaysRemaining = source.GracePeriodDaysRemaining
        };
    }

    private static void TrimOldestTransactions(Dictionary<string, DateTime> transactions, int maxCount)
    {
        if (transactions.Count <= maxCount)
        {
            return;
        }

        var overflow = transactions.Count - maxCount;
        foreach (var key in transactions
                     .OrderBy(pair => pair.Value)
                     .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                     .Take(overflow)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            transactions.Remove(key);
        }
    }

    private static void TrimOldestVerifiedPurchases(
        Dictionary<string, VerifiedPurchaseRecordDto> purchases,
        int maxCount)
    {
        if (purchases.Count <= maxCount)
        {
            return;
        }

        var overflow = purchases.Count - maxCount;
        foreach (var key in purchases
                     .OrderBy(pair => pair.Value.VerifiedAtUtc)
                     .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                     .Take(overflow)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            purchases.Remove(key);
        }
    }

    private static bool DictionariesEqual(
        Dictionary<string, int>? left,
        Dictionary<string, int>? right)
    {
        var leftDict = left ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rightDict = right ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (leftDict.Count != rightDict.Count)
        {
            return false;
        }

        foreach (var (key, leftValue) in leftDict)
        {
            if (!rightDict.TryGetValue(key, out var rightValue) || leftValue != rightValue)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ApplyLifeRegeneration(
        PlayerProgressDto progress,
        DateTime nowUtc,
        GameplayBalanceConfigDto? gameplayBalance = null)
    {
        var changed = false;
        var lifeRegenSeconds = ResolveLifeRegenSeconds(gameplayBalance);

        if (progress.HasInfiniteLives)
        {
            if (progress.InfiniteLivesEndUtc != DateTime.MinValue &&
                nowUtc >= progress.InfiniteLivesEndUtc)
            {
                progress.HasInfiniteLives = false;
                progress.InfiniteLivesEndUtc = DateTime.MinValue;
                changed = true;
            }
            else
            {
                if (progress.NextLifeTimeUtc != DateTime.MinValue)
                {
                    progress.NextLifeTimeUtc = DateTime.MinValue;
                    changed = true;
                }

                return changed;
            }
        }

        if (progress.Lives >= progress.MaxLives)
        {
            if (progress.NextLifeTimeUtc != DateTime.MinValue)
            {
                progress.NextLifeTimeUtc = DateTime.MinValue;
                changed = true;
            }

            return changed;
        }

        if (progress.NextLifeTimeUtc == DateTime.MinValue)
        {
            progress.NextLifeTimeUtc = nowUtc.AddSeconds(lifeRegenSeconds);
            return true;
        }

        if (nowUtc < progress.NextLifeTimeUtc)
        {
            return changed;
        }

        var secondsPast = (nowUtc - progress.NextLifeTimeUtc).TotalSeconds;
        var regainedLives = (int)(secondsPast / lifeRegenSeconds) + 1;
        var previousLives = progress.Lives;
        progress.Lives = Math.Min(progress.MaxLives, progress.Lives + regainedLives);

        if (progress.Lives != previousLives)
        {
            changed = true;
        }

        if (progress.Lives >= progress.MaxLives)
        {
            if (progress.NextLifeTimeUtc != DateTime.MinValue)
            {
                progress.NextLifeTimeUtc = DateTime.MinValue;
                changed = true;
            }
        }
        else
        {
            progress.NextLifeTimeUtc = progress.NextLifeTimeUtc.AddSeconds(regainedLives * lifeRegenSeconds);
            changed = true;
        }

        return changed;
    }

    private static bool ApplyPiggyBankLevelReward(
        PlayerProgressDto progress,
        DateTime nowUtc,
        GameplayBalanceConfigDto? gameplayBalance = null)
    {
        var changed = NormalizePiggyBankState(progress, nowUtc, gameplayBalance);

        if (progress.PiggyBankDurationSeconds > 0 &&
            progress.PiggyBankStartedUtc != DateTime.MinValue &&
            nowUtc >= progress.PiggyBankStartedUtc.AddSeconds(progress.PiggyBankDurationSeconds))
        {
            // Expired piggy bank does not accumulate until explicit reset/collect flow runs.
            return changed;
        }

        if (progress.PiggyBankStartedUtc == DateTime.MinValue)
        {
            progress.PiggyBankStartedUtc = nowUtc;
            changed = true;
        }

        var previousCoins = progress.PiggyBankCoins;
        progress.PiggyBankCoins = Math.Min(
            progress.PiggyBankMaxCoins,
            previousCoins + PiggyBankCoinsPerLevel);

        return changed || progress.PiggyBankCoins != previousCoins;
    }

    private static bool NormalizePiggyBankState(
        PlayerProgressDto progress,
        DateTime nowUtc,
        GameplayBalanceConfigDto? gameplayBalance = null)
    {
        var changed = false;

        if (progress.PiggyBankMaxCoins != PiggyBankMaxCoins)
        {
            progress.PiggyBankMaxCoins = PiggyBankMaxCoins;
            changed = true;
        }

        if (progress.PiggyBankDurationSeconds != PiggyBankDurationSeconds)
        {
            progress.PiggyBankDurationSeconds = PiggyBankDurationSeconds;
            changed = true;
        }

        var clampedCoins = Math.Clamp(progress.PiggyBankCoins, 0, progress.PiggyBankMaxCoins);
        if (clampedCoins != progress.PiggyBankCoins)
        {
            progress.PiggyBankCoins = clampedCoins;
            changed = true;
        }

        if (!IsPiggyBankUnlocked(progress, gameplayBalance))
        {
            if (progress.PiggyBankCoins != 0)
            {
                progress.PiggyBankCoins = 0;
                changed = true;
            }

            if (progress.PiggyBankStartedUtc != DateTime.MinValue)
            {
                progress.PiggyBankStartedUtc = DateTime.MinValue;
                changed = true;
            }

            return changed;
        }

        if (progress.PiggyBankStartedUtc != DateTime.MinValue &&
            progress.PiggyBankDurationSeconds > 0 &&
            nowUtc >= progress.PiggyBankStartedUtc.AddSeconds(progress.PiggyBankDurationSeconds))
        {
            progress.PiggyBankCoins = 0;
            progress.PiggyBankStartedUtc = DateTime.MinValue;
            changed = true;
        }

        if (progress.PiggyBankCoins > 0 && progress.PiggyBankStartedUtc == DateTime.MinValue)
        {
            progress.PiggyBankStartedUtc = nowUtc;
            changed = true;
        }

        return changed;
    }

    private static bool IsPiggyBankUnlocked(PlayerProgressDto progress, GameplayBalanceConfigDto? gameplayBalance)
    {
        return IsPiggyBankUnlocked(progress?.CurrentLevel ?? 1, gameplayBalance);
    }

    private static bool IsPiggyBankUnlocked(int currentLevel, GameplayBalanceConfigDto? gameplayBalance)
    {
        var unlockLevel = ResolveFeatureUnlockLevel(gameplayBalance, PiggyBankFeatureId, DefaultFeatureUnlockLevel);
        return Math.Max(1, currentLevel) >= unlockLevel;
    }

    private static int ResolveFeatureUnlockLevel(
        GameplayBalanceConfigDto? gameplayBalance,
        string featureId,
        int fallbackUnlockLevel)
    {
        var featureGates = gameplayBalance?.FeatureGates;
        if (featureGates == null || featureGates.Length == 0 || string.IsNullOrWhiteSpace(featureId))
        {
            return Math.Max(1, fallbackUnlockLevel);
        }

        for (var i = 0; i < featureGates.Length; i++)
        {
            var rule = featureGates[i];
            if (rule == null)
            {
                continue;
            }

            var rawFeatureId = rule.FeatureId;
            if (string.IsNullOrWhiteSpace(rawFeatureId))
            {
                continue;
            }

            if (!string.Equals(rawFeatureId.Trim(), featureId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rule.UnlockLevel is > 0)
            {
                return rule.UnlockLevel.Value;
            }

            break;
        }

        return Math.Max(1, fallbackUnlockLevel);
    }

    private static int ResolveLifeRegenSeconds()
    {
        var configured = Environment.GetEnvironmentVariable("LIFE_REGEN_SECONDS");
        return int.TryParse(configured, out var seconds) && seconds > 0
            ? seconds
            : DefaultLifeRegenSeconds;
    }

    private static int ResolveLifeRegenSeconds(GameplayBalanceConfigDto? gameplayBalance)
    {
        return gameplayBalance?.LifeRegenTimeSeconds is > 0
            ? gameplayBalance.LifeRegenTimeSeconds.Value
            : LifeRegenSeconds;
    }

    private static int ResolveMaxLives(GameplayBalanceConfigDto? gameplayBalance)
    {
        return gameplayBalance?.MaxLives is > 0
            ? gameplayBalance.MaxLives.Value
            : DefaultMaxLives;
    }

    private static int ResolveStartingLives(GameplayBalanceConfigDto? gameplayBalance, int maxLives)
    {
        var configuredStartingLives = gameplayBalance?.StartingLives is > 0
            ? gameplayBalance.StartingLives.Value
            : DefaultStartingLives;
        return Math.Clamp(configuredStartingLives, 1, Math.Max(1, maxLives));
    }

    private static int ResolveStartingCoins(GameplayBalanceConfigDto? gameplayBalance)
    {
        return gameplayBalance?.StartingCoins is >= 0
            ? gameplayBalance.StartingCoins.Value
            : DefaultStartingCoins;
    }

    private static int ResolveCoinCostPerLife()
    {
        var configured = Environment.GetEnvironmentVariable("COIN_COST_PER_LIFE");
        return int.TryParse(configured, out var value) && value > 0
            ? value
            : DefaultCoinCostPerLife;
    }

    private static BoosterOfferConfig[] ResolveBoosterOffers()
    {
        return
        [
            CreateBoosterOffer(
                "SizeBooster",
                "SizeBooster",
                "SIZE_BOOSTER_COIN_PRICE",
                DefaultSizeBoosterCoinPrice,
                "SIZE_BOOSTER_QUANTITY",
                DefaultBoosterQuantity),
            CreateBoosterOffer(
                "MagnetBooster",
                "MagnetBooster",
                "MAGNET_BOOSTER_COIN_PRICE",
                DefaultMagnetBoosterCoinPrice,
                "MAGNET_BOOSTER_QUANTITY",
                DefaultBoosterQuantity),
            CreateBoosterOffer(
                "TimeBooster",
                "TimeBooster",
                "TIME_BOOSTER_COIN_PRICE",
                DefaultTimeBoosterCoinPrice,
                "TIME_BOOSTER_QUANTITY",
                DefaultBoosterQuantity),
            CreateBoosterOffer(
                "CompassBooster",
                "CompassBooster",
                "COMPASS_BOOSTER_COIN_PRICE",
                DefaultCompassBoosterCoinPrice,
                "COMPASS_BOOSTER_QUANTITY",
                DefaultBoosterQuantity)
        ];
    }

    private static StartBoosterOfferConfig[] ResolveStartBoosterOffers()
    {
        return
        [
            CreateStartBoosterOffer(
                "XpBoost",
                "StartXpBooster",
                "START_XP_BOOST_COIN_PRICE",
                DefaultStartXpBoosterCoinPrice,
                "START_XP_BOOST_QUANTITY",
                DefaultStartBoosterQuantity),
            CreateStartBoosterOffer(
                "PowerBoost",
                "StartPowerBooster",
                "START_POWER_BOOST_COIN_PRICE",
                DefaultStartPowerBoosterCoinPrice,
                "START_POWER_BOOST_QUANTITY",
                DefaultStartBoosterQuantity),
            CreateStartBoosterOffer(
                "TimeBoost",
                "StartTimeBooster",
                "START_TIME_BOOST_COIN_PRICE",
                DefaultStartTimeBoosterCoinPrice,
                "START_TIME_BOOST_QUANTITY",
                DefaultStartBoosterQuantity)
        ];
    }

    private static BoosterOfferConfig CreateBoosterOffer(
        string boosterType,
        string inventoryKey,
        string coinPriceVariableName,
        int defaultCoinPrice,
        string quantityVariableName,
        int defaultQuantity)
    {
        return new BoosterOfferConfig
        {
            BoosterType = boosterType,
            InventoryKey = inventoryKey,
            CoinPrice = ResolvePositiveInt(coinPriceVariableName, defaultCoinPrice),
            Quantity = ResolvePositiveInt(quantityVariableName, defaultQuantity)
        };
    }

    private static StartBoosterOfferConfig CreateStartBoosterOffer(
        string boosterType,
        string inventoryKey,
        string coinPriceVariableName,
        int defaultCoinPrice,
        string quantityVariableName,
        int defaultQuantity)
    {
        return new StartBoosterOfferConfig
        {
            BoosterType = boosterType,
            InventoryKey = inventoryKey,
            CoinPrice = ResolvePositiveInt(coinPriceVariableName, defaultCoinPrice),
            Quantity = ResolvePositiveInt(quantityVariableName, defaultQuantity)
        };
    }

    private static Dictionary<string, BoosterOfferConfig> BuildBoosterOfferLookup(
        IEnumerable<BoosterOfferConfig> offers)
    {
        var lookup = new Dictionary<string, BoosterOfferConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var offer in offers)
        {
            lookup[offer.BoosterType] = offer;
            lookup[offer.InventoryKey] = offer;
        }

        return lookup;
    }

    private static Dictionary<string, StartBoosterOfferConfig> BuildStartBoosterOfferLookup(
        IEnumerable<StartBoosterOfferConfig> offers)
    {
        var lookup = new Dictionary<string, StartBoosterOfferConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var offer in offers)
        {
            lookup[offer.BoosterType] = offer;
            lookup[offer.InventoryKey] = offer;
        }

        return lookup;
    }

    private static bool TryGetBoosterOffer(string boosterType, out BoosterOfferConfig offer)
    {
        return BoosterOfferLookup.TryGetValue(boosterType.Trim(), out offer!);
    }

    private static bool TryGetStartBoosterOffer(string boosterType, out StartBoosterOfferConfig offer)
    {
        return StartBoosterOfferLookup.TryGetValue(boosterType.Trim(), out offer!);
    }

    private static int ResolvePositiveInt(string variableName, int fallback)
    {
        var configured = Environment.GetEnvironmentVariable(variableName);
        return int.TryParse(configured, out var value) && value > 0
            ? value
            : fallback;
    }

    private static int ResolvePiggyBankCoinsPerLevel()
    {
        var configured = Environment.GetEnvironmentVariable("PIGGY_BANK_COINS_PER_LEVEL");
        return int.TryParse(configured, out var value) && value >= 0
            ? value
            : DefaultPiggyBankCoinsPerLevel;
    }

    private static int ResolvePiggyBankMaxCoins()
    {
        var configured = Environment.GetEnvironmentVariable("PIGGY_BANK_MAX_COINS");
        return int.TryParse(configured, out var value) && value > 0
            ? value
            : DefaultPiggyBankMaxCoins;
    }

    private static int ResolvePiggyBankDurationSeconds()
    {
        var configured = Environment.GetEnvironmentVariable("PIGGY_BANK_DURATION_SECONDS");
        return int.TryParse(configured, out var value) && value > 0
            ? value
            : DefaultPiggyBankDurationSeconds;
    }

    private static int ResolveCrownEventCycleDurationSeconds()
    {
        var configured = Environment.GetEnvironmentVariable("CROWN_EVENT_CYCLE_DURATION_SECONDS");
        return int.TryParse(configured, out var value) && value > 0
            ? value
            : DefaultCrownEventCycleDurationSeconds;
    }

    private static int GetServerCoinReward(int levelId)
    {
        return levelId == 3 ? 8 : DefaultLevelCoinReward;
    }

    private static int ClampToInt(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }
}

internal sealed class BoosterOfferConfig
{
    public string BoosterType { get; init; } = string.Empty;
    public string InventoryKey { get; init; } = string.Empty;
    public int CoinPrice { get; init; }
    public int Quantity { get; init; }
}

internal sealed class StartBoosterOfferConfig
{
    public string BoosterType { get; init; } = string.Empty;
    public string InventoryKey { get; init; } = string.Empty;
    public int CoinPrice { get; init; }
    public int Quantity { get; init; }
}

/// <summary>
/// Result of a daily-gift claim operation.
/// </summary>
public sealed class DailyGiftClaimResult
{
    public PlayerProgressDto Progress { get; }
    public bool WasDuplicate { get; }
    public bool StreakReset { get; }
    public int ClaimedDay { get; }
    public DailyGiftRewardDto Reward { get; }

    private DailyGiftClaimResult(
        PlayerProgressDto progress,
        bool wasDuplicate,
        bool streakReset,
        int claimedDay,
        DailyGiftRewardDto reward)
    {
        Progress = progress;
        WasDuplicate = wasDuplicate;
        StreakReset = streakReset;
        ClaimedDay = claimedDay;
        Reward = reward;
    }

    public static DailyGiftClaimResult Granted(
        PlayerProgressDto progress,
        int claimedDay,
        bool streakReset,
        DailyGiftRewardDto reward) =>
        new(progress, wasDuplicate: false, streakReset, claimedDay, reward);

    public static DailyGiftClaimResult Duplicate(PlayerProgressDto progress, int claimedDay) =>
        new(progress, wasDuplicate: true, streakReset: false, claimedDay, new DailyGiftRewardDto());
}

/// <summary>
/// Result of a merge operation.
/// </summary>
public sealed class MergeResult
{
    public bool IsSuccess { get; }
    public PlayerProgressDto? NewProgress { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    private MergeResult(bool isSuccess, PlayerProgressDto? newProgress, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        NewProgress = newProgress;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static MergeResult Success(PlayerProgressDto newProgress) =>
        new(true, newProgress, null, null);

    public static MergeResult Failure(string errorCode, string message) =>
        new(false, null, errorCode, message);
}
