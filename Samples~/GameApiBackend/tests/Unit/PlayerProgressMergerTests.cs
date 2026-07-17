using System;
using System.Collections.Generic;
using Serhat.Forge.CloudScript.Domain;
using Serhat.Forge.CloudScript.Domain.DTOs;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests;

public sealed class PlayerProgressMergerTests
{
    [Fact]
    public void ApplyLevelResult_HappyPath_UpdatesProgress()
    {
        var progress = CreateTestProgress(currentLevel: 1);
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 3,
            TimeSec = 42.5f,
            CrownsCollected = 0
        };

        var result = PlayerProgressMerger.ApplyLevelResult(progress, request);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.NewProgress!.CurrentLevel);
        Assert.True(result.NewProgress.Results.ContainsKey("1"));
        Assert.Equal(3, result.NewProgress.Results["1"].Stars);
        Assert.Equal(42.5f, result.NewProgress.Results["1"].TimeSec);
    }

    [Fact]
    public void ApplyLevelResult_InvalidLevel_ReturnsFailure()
    {
        var progress = CreateTestProgress(currentLevel: 2);
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 2,
            TimeSec = 30f,
            CrownsCollected = 0
        };

        var result = PlayerProgressMerger.ApplyLevelResult(progress, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.InvalidLevel, result.ErrorCode);
    }

    [Fact]
    public void ApplyLevelResult_AlreadyCompleted_ReturnsFailure()
    {
        var progress = CreateTestProgress(currentLevel: 1);
        progress.Results["1"] = new LevelResultDto { Stars = 1, TimeSec = 50f };
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 2,
            TimeSec = 45f,
            CrownsCollected = 0
        };

        var result = PlayerProgressMerger.ApplyLevelResult(progress, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AlreadyCompleted, result.ErrorCode);
    }

    [Fact]
    public void BuyLivesWithCoins_RefillsMissingLivesAndSpendsCoins()
    {
        var progress = CreateTestProgress(currentLevel: 3, lives: 3, maxLives: 5, coins: 500);
        progress.NextLifeTimeUtc = DateTime.UtcNow.AddMinutes(10);

        var result = PlayerProgressMerger.BuyLivesWithCoins(progress, new BuyLivesWithCoinsRequestDto());

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.NewProgress!.Lives);
        Assert.Equal(300, result.NewProgress.Coins);
        Assert.Equal(DateTime.MinValue, result.NewProgress.NextLifeTimeUtc);
        Assert.Equal(2, result.NewProgress.StateVersion);
    }

    [Fact]
    public void BuyLivesWithCoins_InsufficientFunds_ReturnsFailure()
    {
        var progress = CreateTestProgress(currentLevel: 3, lives: 2, maxLives: 5, coins: 150);

        var result = PlayerProgressMerger.BuyLivesWithCoins(progress, new BuyLivesWithCoinsRequestDto());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.InsufficientFunds, result.ErrorCode);
    }

    [Fact]
    public void GrantAdRewardLife_AddsOneLife_AndClearsTimerWhenFull()
    {
        var progress = CreateTestProgress(currentLevel: 4, lives: 4, maxLives: 5, coins: 250);
        progress.NextLifeTimeUtc = DateTime.UtcNow.AddMinutes(8);

        var result = PlayerProgressMerger.GrantAdRewardLife(progress, new GrantAdRewardLifeRequestDto());

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.NewProgress!.Lives);
        Assert.Equal(DateTime.MinValue, result.NewProgress.NextLifeTimeUtc);
        Assert.Equal(2, result.NewProgress.StateVersion);
    }

    [Fact]
    public void CreateDefaultProgress_HasCorrectDefaults()
    {
        var progress = PlayerProgressMerger.CreateDefaultProgress("player123");

        Assert.Equal("player123", progress.PlayerId);
        Assert.Equal(1, progress.CurrentLevel);
        Assert.Equal(1, progress.StateVersion);
        Assert.Empty(progress.Results);
        Assert.NotNull(progress.CrownEvent);
        Assert.Equal(0, progress.CrownEvent.CycleIndex);
        Assert.Equal(0, progress.CrownEvent.CrownsInCycle);
    }

    [Fact]
    public void ApplyClientState_StaleVersion_DoesNotApplyMutableFieldChanges()
    {
        var current = CreateTestProgress(currentLevel: 5, lives: 5, maxLives: 5, coins: 1000);
        current.StateVersion = 12;
        current.WinStreak = 4;
        current.BoostersOwned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["SizeBooster"] = 3,
            ["MagnetBooster"] = 2,
            ["SpeedBooster"] = 2,
            ["TimeBooster"] = 2,
            ["CompassBooster"] = 3,
            ["StartXpBooster"] = 1,
            ["StartPowerBooster"] = 1,
            ["StartTimeBooster"] = 1
        };
        current.BoostersFree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["SizeBooster"] = 1
        };

        var staleClientState = new PlayerProgressDto
        {
            PlayerId = current.PlayerId,
            CurrentLevel = current.CurrentLevel,
            StateVersion = 11,
            Lives = 2,
            MaxLives = current.MaxLives,
            Coins = 250,
            WinStreak = 0,
            BoostersOwned = new Dictionary<string, int>(current.BoostersOwned, StringComparer.OrdinalIgnoreCase)
            {
                ["SizeBooster"] = 2,
                ["CompassBooster"] = 2
            },
            BoostersFree = new Dictionary<string, int>(current.BoostersFree, StringComparer.OrdinalIgnoreCase)
            {
                ["SizeBooster"] = 0
            }
        };

        var result = PlayerProgressMerger.ApplyClientState(current, staleClientState);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.NewProgress);
        Assert.Equal(5, result.NewProgress!.Lives);
        Assert.Equal(1000, result.NewProgress.Coins);
        Assert.Equal(4, result.NewProgress.WinStreak);
        Assert.Equal(3, result.NewProgress.BoostersOwned["SizeBooster"]);
        Assert.Equal(3, result.NewProgress.BoostersOwned["CompassBooster"]);
        Assert.Equal(1, result.NewProgress.BoostersFree["SizeBooster"]);
        // Server-side normalization may advance the version even when every stale
        // client mutation is ignored. The security invariant is monotonicity.
        Assert.True(result.NewProgress.StateVersion >= current.StateVersion);
    }

    [Fact]
    public void ApplyLevelResult_CrownsCollected_UpdatesCrownEventProgress()
    {
        var progress = CreateTestProgress(currentLevel: 1);
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 2,
            TimeSec = 33.3f,
            CrownsCollected = 3
        };

        var result = PlayerProgressMerger.ApplyLevelResult(progress, request);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.NewProgress!.CrownEvent.CycleIndex);
        Assert.Equal(2, result.NewProgress.CrownEvent.CrownsInCycle);
        Assert.Contains(1, result.NewProgress.CrownEvent.ClaimedMilestones);
    }

    [Fact]
    public void ApplyLevelResult_CrownMilestoneProgress_UsesPerMilestoneResetThresholds()
    {
        var progress = CreateTestProgress(currentLevel: 1);
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 2,
            TimeSec = 19.5f,
            CrownsCollected = 11
        };

        var result = PlayerProgressMerger.ApplyLevelResult(progress, request);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.NewProgress!.CrownEvent.CycleIndex);
        Assert.Equal(6, result.NewProgress.CrownEvent.CrownsInCycle); // milestone3 target is 7 (after 1 + 4 completed)
        Assert.Equal(2, result.NewProgress.CrownEvent.ClaimedMilestones.Count);
        Assert.Contains(1, result.NewProgress.CrownEvent.ClaimedMilestones);
        Assert.Contains(2, result.NewProgress.CrownEvent.ClaimedMilestones);
        Assert.False(result.NewProgress.HasInfiniteLives);
    }

    [Fact]
    public void ApplyLevelResult_CrownsCollectedZero_DoesNotAdvanceCrownEventProgress()
    {
        var progress = CreateTestProgress(currentLevel: 1);
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 1,
            TimeSec = 18f,
            CrownsCollected = 0
        };

        var result = PlayerProgressMerger.ApplyLevelResult(progress, request);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.NewProgress!.CrownEvent.CycleIndex);
        Assert.Equal(0, result.NewProgress.CrownEvent.CrownsInCycle);
        Assert.Empty(result.NewProgress.CrownEvent.ClaimedMilestones);
    }

    [Fact]
    public void ApplyLevelResult_CrownMilestones_GrantConfiguredRewards()
    {
        var progress = CreateTestProgress(currentLevel: 1, coins: 100);
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 3,
            TimeSec = 21f,
            CrownsCollected = 22
        };

        var before = DateTime.UtcNow;
        var result = PlayerProgressMerger.ApplyLevelResult(progress, request);

        Assert.True(result.IsSuccess);
        Assert.Equal(310, result.NewProgress!.Coins); // 100 + level(10) + crown milestone(200)
        Assert.Equal(310, result.NewProgress.TotalCoinsEarned);
        Assert.True(result.NewProgress.HasInfiniteLives);
        Assert.True(result.NewProgress.InfiniteLivesEndUtc >= before.AddMinutes(14));
        Assert.Equal(1, result.NewProgress.BoostersOwned["StartTimeBooster"]);
        Assert.Equal(2, result.NewProgress.BoostersOwned["StartXpBooster"]);
        Assert.Equal(0, result.NewProgress.CrownEvent.CycleIndex);
        Assert.Equal(0, result.NewProgress.CrownEvent.CrownsInCycle);
        Assert.Equal(4, result.NewProgress.CrownEvent.ClaimedMilestones.Count);
        Assert.Contains(1, result.NewProgress.CrownEvent.ClaimedMilestones);
        Assert.Contains(2, result.NewProgress.CrownEvent.ClaimedMilestones);
        Assert.Contains(3, result.NewProgress.CrownEvent.ClaimedMilestones);
        Assert.Contains(4, result.NewProgress.CrownEvent.ClaimedMilestones);
    }

    [Fact]
    public void ApplyLevelResult_CrownCycleRollover_ResetsMilestonesAndContinuesNextCycle()
    {
        var progress = CreateTestProgress(currentLevel: 1);
        var config = PlayerProgressMerger.GetCurrentCrownEventConfig();
        config.CycleDurationSeconds = 300;
        progress.CrownEvent = new CrownEventStateDto
        {
            CycleIndex = 0,
            CrownsInCycle = 0,
            ClaimedMilestones = BuildClaimedMilestoneRange(24),
            CycleStartedUtc = DateTime.UtcNow.AddMinutes(-1),
            CycleDurationSeconds = config.CycleDurationSeconds,
            LastUpdatedUtc = DateTime.UtcNow.AddMinutes(-5)
        };

        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 2,
            TimeSec = 15f,
            CrownsCollected = 2
        };

        var before = DateTime.UtcNow;
        var result = PlayerProgressMerger.ApplyLevelResult(progress, request, crownEventConfig: config);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.NewProgress!.CrownEvent.CycleIndex);
        Assert.Equal(0, result.NewProgress.CrownEvent.CrownsInCycle);
        Assert.Single(result.NewProgress.CrownEvent.ClaimedMilestones);
        Assert.Contains(1, result.NewProgress.CrownEvent.ClaimedMilestones);
        Assert.Equal(config.CycleDurationSeconds, result.NewProgress.CrownEvent.CycleDurationSeconds);
        Assert.True(result.NewProgress.CrownEvent.CycleStartedUtc >= before);
    }

    [Fact]
    public void ApplyLevelResult_CrownCycleExpired_ResetsProgressAndRestartsTimer()
    {
        var config = PlayerProgressMerger.GetCurrentCrownEventConfig();
        config.CycleDurationSeconds = 60;
        var progress = CreateTestProgress(currentLevel: 1);
        progress.CrownEvent = new CrownEventStateDto
        {
            CycleIndex = 0,
            CrownsInCycle = 3,
            ClaimedMilestones = new List<int> { 1, 2 },
            CycleStartedUtc = DateTime.UtcNow.AddMinutes(-10),
            CycleDurationSeconds = config.CycleDurationSeconds,
            LastUpdatedUtc = DateTime.UtcNow.AddMinutes(-9)
        };

        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 1,
            TimeSec = 12f,
            CrownsCollected = 0
        };

        var before = DateTime.UtcNow;
        var result = PlayerProgressMerger.ApplyLevelResult(progress, request, crownEventConfig: config);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.NewProgress!.CrownEvent.CycleIndex);
        Assert.Equal(0, result.NewProgress.CrownEvent.CrownsInCycle);
        Assert.Empty(result.NewProgress.CrownEvent.ClaimedMilestones);
        Assert.Equal(config.CycleDurationSeconds, result.NewProgress.CrownEvent.CycleDurationSeconds);
        Assert.True(result.NewProgress.CrownEvent.CycleStartedUtc >= before);
    }

    [Fact]
    public void GetCurrentCrownEventConfig_DefaultContainsTwentyFiveMilestones()
    {
        var config = PlayerProgressMerger.GetCurrentCrownEventConfig();

        Assert.Equal(25, config.Milestones.Count);
        Assert.Equal(133, config.CrownsPerCycle);
        Assert.True(config.CycleDurationSeconds > 0);
    }

    private static PlayerProgressDto CreateTestProgress(
        int currentLevel,
        int lives = 5,
        int maxLives = 5,
        int coins = 100)
    {
        var crownConfig = PlayerProgressMerger.GetCurrentCrownEventConfig();
        var now = DateTime.UtcNow;

        return new PlayerProgressDto
        {
            PlayerId = "test-player",
            CurrentLevel = currentLevel,
            StateVersion = 1,
            Lives = lives,
            MaxLives = maxLives,
            Coins = coins,
            TotalCoinsEarned = Math.Max(100, coins),
            Results = new Dictionary<string, LevelResultDto>(),
            CrownEvent = new CrownEventStateDto
            {
                CycleStartedUtc = now,
                CycleDurationSeconds = crownConfig.CycleDurationSeconds,
                LastUpdatedUtc = now
            }
        };
    }

    private static List<int> BuildClaimedMilestoneRange(int endInclusive)
    {
        var claimed = new List<int>(Math.Max(0, endInclusive));
        for (var milestone = 1; milestone <= endInclusive; milestone++)
        {
            claimed.Add(milestone);
        }

        return claimed;
    }
}
