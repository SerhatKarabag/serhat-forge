using System;
using System.Collections.Generic;
using Serhat.Forge.CloudScript.Domain.DTOs;

namespace Serhat.Forge.CloudScript.Domain;

/// <summary>
/// Server-side mapping of store product IDs to reward metadata.
/// Keep this in sync with SerhatForgeProductCatalog on client.
/// </summary>
public static class PurchaseRewardCatalog
{
    private const string TierBasic = "holepass_basic";
    private const string TierPremium = "holepass_premium";

    private static readonly Dictionary<string, string> MetadataByProductId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["com.serhat.forge.coins_1k"] = "coins:1000",
            ["com.serhat.forge.coins_5k"] = "coins:5000",
            ["com.serhat.forge.coins_10k"] = "coins:10000",
            ["com.serhat.forge.coins_25k"] = "coins:25000",
            ["com.serhat.forge.coins_50k"] = "coins:50000",
            ["com.serhat.forge.coins_100k"] = "coins:100000",

            ["com.serhat.forge.life_refill"] = "life_refill",
            ["com.serhat.forge.infinite_lives_1h"] = "infinite_lives:3600",
            ["com.serhat.forge.infinite_lives_3h"] = "infinite_lives:10800",
            ["com.serhat.forge.infinite_lives_24h"] = "infinite_lives:86400",

            ["com.serhat.forge.booster_size_3"] = "booster:SizeBooster:3",
            ["com.serhat.forge.booster_magnet_3"] = "booster:MagnetBooster:3",
            ["com.serhat.forge.booster_speed_3"] = "booster:SpeedBooster:3",
            ["com.serhat.forge.booster_time_3"] = "booster:TimeBooster:3",
            ["com.serhat.forge.booster_compass_3"] = "booster:CompassBooster:3",

            ["com.serhat.forge.booster_size_5"] = "booster:SizeBooster:5",
            ["com.serhat.forge.booster_magnet_5"] = "booster:MagnetBooster:5",
            ["com.serhat.forge.booster_speed_5"] = "booster:SpeedBooster:5",
            ["com.serhat.forge.booster_time_5"] = "booster:TimeBooster:5",
            ["com.serhat.forge.booster_compass_5"] = "booster:CompassBooster:5",

            ["com.serhat.forge.booster_size_10"] = "booster:SizeBooster:10",
            ["com.serhat.forge.booster_magnet_10"] = "booster:MagnetBooster:10",
            ["com.serhat.forge.booster_speed_10"] = "booster:SpeedBooster:10",
            ["com.serhat.forge.booster_time_10"] = "booster:TimeBooster:10",
            ["com.serhat.forge.booster_compass_10"] = "booster:CompassBooster:10",

            ["com.serhat.forge.bundle_mini"] =
                "bundle:coins:5000|booster:SizeBooster:1|booster:MagnetBooster:1|booster:SpeedBooster:1|booster:TimeBooster:1|booster:CompassBooster:1|booster:StartXpBooster:1|booster:StartPowerBooster:1|booster:StartTimeBooster:1|infinite_lives:3600",
            ["com.serhat.forge.bundle_master"] =
                "bundle:coins:10000|booster:SizeBooster:2|booster:MagnetBooster:2|booster:SpeedBooster:2|booster:TimeBooster:2|booster:CompassBooster:2|booster:StartXpBooster:2|booster:StartPowerBooster:2|booster:StartTimeBooster:2|infinite_lives:7200",
            ["com.serhat.forge.bundle_deluxe"] =
                "bundle:coins:25000|booster:SizeBooster:4|booster:MagnetBooster:4|booster:SpeedBooster:4|booster:TimeBooster:4|booster:CompassBooster:4|booster:StartXpBooster:4|booster:StartPowerBooster:4|booster:StartTimeBooster:4|infinite_lives:21600",
            ["com.serhat.forge.remove_ads_bundle"] =
                "bundle:remove_ads|coins:2500|booster:SizeBooster:1|booster:MagnetBooster:1|booster:SpeedBooster:1|booster:TimeBooster:1|booster:CompassBooster:1|booster:StartPowerBooster:1|booster:StartTimeBooster:1",

            ["com.serhat.forge.holepass_basic"] = "holepass:basic",
            ["com.serhat.forge.holepass_premium"] = "holepass:premium",

            ["com.serhat.forge.piggybank"] = "piggy_bank_collect"
        };

    public static bool TryApplyRewards(
        PlayerProgressDto progress,
        GrantPurchaseRewardsRequestDto request,
        out string errorCode,
        out string errorMessage)
    {
        errorCode = string.Empty;
        errorMessage = string.Empty;

        if (!MetadataByProductId.TryGetValue(request.ProductId, out var metadata))
        {
            errorCode = ErrorCodes.ValidationFailed;
            errorMessage = $"Unsupported product id: {request.ProductId}";
            return false;
        }

        progress.BoostersOwned ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        progress.BoostersFree ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        return TryApplyMetadata(progress, metadata, request, out errorCode, out errorMessage);
    }

    public static bool IsSupportedProduct(string productId)
    {
        return MetadataByProductId.ContainsKey(productId);
    }

    public static bool TryGetRewardMetadata(string productId, out string metadata)
    {
        return MetadataByProductId.TryGetValue(productId, out metadata!);
    }

    public static bool TryGetProductType(string productId, out ProductTypeCode productType)
    {
        productType = ProductTypeCode.Consumable;
        if (!MetadataByProductId.TryGetValue(productId, out var metadata))
        {
            return false;
        }

        productType = metadata.StartsWith("holepass:", StringComparison.OrdinalIgnoreCase)
            ? ProductTypeCode.Subscription
            : ProductTypeCode.Consumable;
        return true;
    }

    public static bool TryGetSubscriptionTierKey(string productId, out string tierKey)
    {
        tierKey = string.Empty;
        if (!MetadataByProductId.TryGetValue(productId, out var metadata) ||
            !metadata.StartsWith("holepass:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawTier = metadata["holepass:".Length..].Trim();
        tierKey = NormalizeTierKey(rawTier);
        return !string.IsNullOrWhiteSpace(tierKey);
    }

    private static bool TryApplyMetadata(
        PlayerProgressDto progress,
        string metadata,
        GrantPurchaseRewardsRequestDto request,
        out string errorCode,
        out string errorMessage)
    {
        errorCode = string.Empty;
        errorMessage = string.Empty;

        if (metadata.StartsWith("bundle:", StringComparison.OrdinalIgnoreCase))
        {
            var rewardsPart = metadata["bundle:".Length..];
            var tokens = rewardsPart.Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (!TryApplySingleReward(progress, token.Trim(), request, out errorCode, out errorMessage))
                {
                    return false;
                }
            }

            return true;
        }

        return TryApplySingleReward(progress, metadata, request, out errorCode, out errorMessage);
    }

    private static bool TryApplySingleReward(
        PlayerProgressDto progress,
        string token,
        GrantPurchaseRewardsRequestDto request,
        out string errorCode,
        out string errorMessage)
    {
        errorCode = string.Empty;
        errorMessage = string.Empty;

        if (token.StartsWith("coins:", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(token["coins:".Length..], out var amount) || amount <= 0)
            {
                errorCode = ErrorCodes.ValidationFailed;
                errorMessage = $"Invalid coins token: {token}";
                return false;
            }

            progress.Coins = ClampToInt((long)progress.Coins + amount);
            progress.TotalCoinsEarned = ClampToInt((long)progress.TotalCoinsEarned + amount);
            return true;
        }

        if (string.Equals(token, "life_refill", StringComparison.OrdinalIgnoreCase))
        {
            progress.Lives = Math.Max(0, progress.MaxLives);
            progress.NextLifeTimeUtc = DateTime.MinValue;
            return true;
        }

        if (string.Equals(token, "remove_ads", StringComparison.OrdinalIgnoreCase))
        {
            progress.HasRemovedAds = true;
            return true;
        }

        if (token.StartsWith("infinite_lives:", StringComparison.OrdinalIgnoreCase))
        {
            if (!double.TryParse(token["infinite_lives:".Length..], out var seconds) || seconds <= 0)
            {
                errorCode = ErrorCodes.ValidationFailed;
                errorMessage = $"Invalid infinite lives token: {token}";
                return false;
            }

            progress.HasInfiniteLives = true;
            progress.NextLifeTimeUtc = DateTime.MinValue;

            var now = DateTime.UtcNow;
            var baseEnd = progress.InfiniteLivesEndUtc > now ? progress.InfiniteLivesEndUtc : now;
            progress.InfiniteLivesEndUtc = baseEnd.AddSeconds(seconds);
            return true;
        }

        if (token.StartsWith("booster:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = token.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !int.TryParse(parts[2], out var count) || count <= 0)
            {
                errorCode = ErrorCodes.ValidationFailed;
                errorMessage = $"Invalid booster token: {token}";
                return false;
            }

            var boosterType = parts[1];
            progress.BoostersOwned.TryGetValue(boosterType, out var currentCount);
            progress.BoostersOwned[boosterType] = ClampToInt((long)currentCount + count);
            return true;
        }

        if (token.StartsWith("holepass:", StringComparison.OrdinalIgnoreCase))
        {
            var tokenTier = token["holepass:".Length..].Trim();
            return TryApplyHolePassReward(progress, request, tokenTier, out errorCode, out errorMessage);
        }

        if (string.Equals(token, "piggy_bank_collect", StringComparison.OrdinalIgnoreCase))
        {
            return TryApplyPiggyBankCollect(progress, out errorCode, out errorMessage);
        }

        errorCode = ErrorCodes.ValidationFailed;
        errorMessage = $"Unsupported reward token: {token}";
        return false;
    }

    private static bool TryApplyHolePassReward(
        PlayerProgressDto progress,
        GrantPurchaseRewardsRequestDto request,
        string fallbackTierFromProduct,
        out string errorCode,
        out string errorMessage)
    {
        errorCode = string.Empty;
        errorMessage = string.Empty;

        progress.HasInfiniteLives = true;
        progress.InfiniteLivesEndUtc = DateTime.MinValue;
        progress.NextLifeTimeUtc = DateTime.MinValue;

        var rawTier = string.IsNullOrWhiteSpace(request.TierKey)
            ? fallbackTierFromProduct
            : request.TierKey;
        var tier = NormalizeTierKey(rawTier);
        if (string.Equals(tier, TierPremium, StringComparison.OrdinalIgnoreCase))
        {
            progress.Coins = ClampToInt((long)progress.Coins + 1000);
            progress.TotalCoinsEarned = ClampToInt((long)progress.TotalCoinsEarned + 1000);
            AddBooster(progress, "SizeBooster", 5);
            AddBooster(progress, "MagnetBooster", 5);
            AddBooster(progress, "TimeBooster", 5);
            return true;
        }

        if (string.Equals(tier, TierBasic, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(tier))
        {
            progress.Coins = ClampToInt((long)progress.Coins + 500);
            progress.TotalCoinsEarned = ClampToInt((long)progress.TotalCoinsEarned + 500);
            AddBooster(progress, "SizeBooster", 3);
            AddBooster(progress, "MagnetBooster", 3);
            return true;
        }

        errorCode = ErrorCodes.ValidationFailed;
        errorMessage = $"Unsupported subscription tier: {tier}";
        return false;
    }

    private static string NormalizeTierKey(string rawTier)
    {
        if (string.IsNullOrWhiteSpace(rawTier))
        {
            return string.Empty;
        }

        if (string.Equals(rawTier, "basic", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawTier, TierBasic, StringComparison.OrdinalIgnoreCase))
        {
            return TierBasic;
        }

        if (string.Equals(rawTier, "premium", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawTier, TierPremium, StringComparison.OrdinalIgnoreCase))
        {
            return TierPremium;
        }

        return rawTier.Trim();
    }

    private static bool TryApplyPiggyBankCollect(
        PlayerProgressDto progress,
        out string errorCode,
        out string errorMessage)
    {
        errorCode = string.Empty;
        errorMessage = string.Empty;

        var piggyCoins = Math.Max(0, progress.PiggyBankCoins);
        if (piggyCoins <= 0)
        {
            errorCode = ErrorCodes.ValidationFailed;
            errorMessage = "Piggy bank is empty, nothing to collect.";
            return false;
        }

        // Check if piggy bank has expired
        if (progress.PiggyBankStartedUtc != DateTime.MinValue &&
            progress.PiggyBankDurationSeconds > 0)
        {
            var expiryUtc = progress.PiggyBankStartedUtc.AddSeconds(progress.PiggyBankDurationSeconds);
            if (DateTime.UtcNow >= expiryUtc)
            {
                // Expired — reset and reject
                progress.PiggyBankCoins = 0;
                progress.PiggyBankStartedUtc = DateTime.MinValue;
                errorCode = ErrorCodes.ValidationFailed;
                errorMessage = "Piggy bank has expired. Coins have been reset.";
                return false;
            }
        }

        // Grant piggy bank coins to player balance
        progress.Coins = ClampToInt((long)progress.Coins + piggyCoins);
        progress.TotalCoinsEarned = ClampToInt((long)progress.TotalCoinsEarned + piggyCoins);

        // Reset piggy bank for next cycle
        progress.PiggyBankCoins = 0;
        progress.PiggyBankStartedUtc = DateTime.MinValue;

        return true;
    }

    private static void AddBooster(PlayerProgressDto progress, string boosterType, int amount)
    {
        progress.BoostersOwned.TryGetValue(boosterType, out var currentCount);
        progress.BoostersOwned[boosterType] = ClampToInt((long)currentCount + amount);
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
