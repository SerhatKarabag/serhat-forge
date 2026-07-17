using System;
using System.Collections.Generic;
using System.Text.Json;
using Serhat.Forge.CloudScript.Domain.DTOs;

namespace Serhat.Forge.CloudScript.Domain;

/// <summary>
/// Parses gameplay-balance title data while protecting bootstrap from malformed payloads.
/// </summary>
public static class GameplayBalanceTitleDataParser
{
    public const string TitleDataKey = "game_balance_v1";
    private const int MaxMultiplierCount = 256;
    private const int MaxSizeXpCount = 512;
    private const int MaxFeatureGateRuleCount = 64;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(
        string? json,
        out GameplayBalanceConfigDto balance,
        out string? error)
    {
        balance = new GameplayBalanceConfigDto();
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        GameplayBalanceTitleDataModel? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GameplayBalanceTitleDataModel>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }

        if (payload == null)
        {
            error = "Title data payload is null.";
            return false;
        }

        balance.Version = payload.Version > 0 ? payload.Version : 1;

        if (!TryNormalizePositiveFloat(payload.Speed, out var speed, out error))
        {
            error = $"Invalid speed: {error}";
            return false;
        }

        if (!TryNormalizeUnitFloat(payload.Smoothness, out var smoothness, out error))
        {
            error = $"Invalid smoothness: {error}";
            return false;
        }

        if (!TryNormalizePositiveFloat(payload.HoleRotationSmoothness, out var holeRotationSmoothness, out error))
        {
            error = $"Invalid holeRotationSmoothness: {error}";
            return false;
        }

        if (!TryNormalizePositiveInt(payload.LifeRegenTimeSeconds, out var lifeRegenTimeSeconds, out error))
        {
            error = $"Invalid lifeRegenTimeSeconds: {error}";
            return false;
        }

        if (!TryNormalizePositiveInt(payload.MaxLives, out var maxLives, out error))
        {
            error = $"Invalid maxLives: {error}";
            return false;
        }

        if (!TryNormalizePositiveInt(payload.StartingLives, out var startingLives, out error))
        {
            error = $"Invalid startingLives: {error}";
            return false;
        }

        if (!TryNormalizeNonNegativeInt(payload.StartingCoins, out var startingCoins, out error))
        {
            error = $"Invalid startingCoins: {error}";
            return false;
        }

        if (!TryNormalizeNonNegativeInt(payload.InterstitialAfterLevel, out var interstitialAfterLevel, out error))
        {
            error = $"Invalid interstitialAfterLevel: {error}";
            return false;
        }

        if (!TryNormalizeFeatureGates(payload.FeatureGates, out var featureGates, out error))
        {
            error = $"Invalid featureGates: {error}";
            return false;
        }

        if (!TryNormalizeSizeXp(payload.SizeXp, out var sizeXp, out error))
        {
            error = $"Invalid sizeXp: {error}";
            return false;
        }

        if (!TryNormalizeMultipliers(payload.HoleScaleMultipliers, out var scaleMultipliers, out error))
        {
            error = $"Invalid holeScaleMultipliers: {error}";
            return false;
        }

        if (!TryNormalizeMultipliers(payload.HoleSpeedMultipliers, out var speedMultipliers, out error))
        {
            error = $"Invalid holeSpeedMultipliers: {error}";
            return false;
        }

        balance.Speed = speed;
        balance.Smoothness = smoothness;
        balance.HoleRotationSmoothness = holeRotationSmoothness;
        balance.LifeRegenTimeSeconds = lifeRegenTimeSeconds;
        balance.MaxLives = maxLives;
        balance.StartingLives = startingLives;
        balance.StartingCoins = startingCoins;
        balance.InterstitialAfterLevel = interstitialAfterLevel;
        balance.FeatureGates = featureGates;
        balance.SizeXp = sizeXp;
        balance.HoleScaleMultipliers = scaleMultipliers;
        balance.HoleSpeedMultipliers = speedMultipliers;
        error = null;
        return true;
    }

    private static bool TryNormalizePositiveFloat(
        float? source,
        out float? normalized,
        out string? error)
    {
        normalized = null;
        error = null;

        if (!source.HasValue)
        {
            return true;
        }

        var value = source.Value;
        if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
        {
            error = "value must be a finite number greater than 0.";
            return false;
        }

        normalized = value;
        return true;
    }

    private static bool TryNormalizeUnitFloat(
        float? source,
        out float? normalized,
        out string? error)
    {
        normalized = null;
        error = null;

        if (!source.HasValue)
        {
            return true;
        }

        var value = source.Value;
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
        {
            error = "value must be a finite number between 0 and 1.";
            return false;
        }

        normalized = value;
        return true;
    }

    private static bool TryNormalizePositiveInt(
        int? source,
        out int? normalized,
        out string? error)
    {
        normalized = null;
        error = null;

        if (!source.HasValue)
        {
            return true;
        }

        if (source.Value <= 0)
        {
            error = "value must be greater than 0.";
            return false;
        }

        normalized = source.Value;
        return true;
    }

    private static bool TryNormalizeNonNegativeInt(
        int? source,
        out int? normalized,
        out string? error)
    {
        normalized = null;
        error = null;

        if (!source.HasValue)
        {
            return true;
        }

        if (source.Value < 0)
        {
            error = "value must be 0 or greater.";
            return false;
        }

        normalized = source.Value;
        return true;
    }

    private static bool TryNormalizeMultipliers(
        float[]? source,
        out float[] normalized,
        out string? error)
    {
        normalized = Array.Empty<float>();
        error = null;

        if (source == null || source.Length == 0)
        {
            return true;
        }

        if (source.Length > MaxMultiplierCount)
        {
            error = $"count exceeds max {MaxMultiplierCount}.";
            return false;
        }

        normalized = new float[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var value = source[i];
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                error = $"value at index {i} must be a finite number greater than 0.";
                return false;
            }

            normalized[i] = value;
        }

        return true;
    }

    private static bool TryNormalizeSizeXp(
        int[]? source,
        out int[] normalized,
        out string? error)
    {
        normalized = Array.Empty<int>();
        error = null;

        if (source == null || source.Length == 0)
        {
            return true;
        }

        if (source.Length > MaxSizeXpCount)
        {
            error = $"count exceeds max {MaxSizeXpCount}.";
            return false;
        }

        normalized = new int[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var value = source[i];
            if (value < 0)
            {
                error = $"value at index {i} must be 0 or greater.";
                return false;
            }

            if (i > 0 && value < source[i - 1])
            {
                error = $"value at index {i} must be greater than or equal to the previous threshold.";
                return false;
            }

            normalized[i] = value;
        }

        return true;
    }

    private static bool TryNormalizeFeatureGates(
        FeatureGateTitleDataModel[]? source,
        out GameplayFeatureGateRuleDto[] normalized,
        out string? error)
    {
        normalized = Array.Empty<GameplayFeatureGateRuleDto>();
        error = null;

        if (source == null || source.Length == 0)
        {
            return true;
        }

        if (source.Length > MaxFeatureGateRuleCount)
        {
            error = $"count exceeds max {MaxFeatureGateRuleCount}.";
            return false;
        }

        var featureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<GameplayFeatureGateRuleDto>(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var entry = source[i];
            if (entry == null)
            {
                error = $"entry at index {i} is null.";
                return false;
            }

            var featureId = entry.FeatureId?.Trim();
            if (string.IsNullOrWhiteSpace(featureId))
            {
                error = $"featureId at index {i} is required.";
                return false;
            }

            if (!featureIds.Add(featureId))
            {
                error = $"duplicate featureId '{featureId}'.";
                return false;
            }

            if (entry.UnlockLevel.HasValue && entry.UnlockLevel.Value <= 0)
            {
                error = $"unlockLevel at index {i} must be greater than 0 when provided.";
                return false;
            }

            list.Add(new GameplayFeatureGateRuleDto
            {
                FeatureId = featureId,
                UnlockLevel = entry.UnlockLevel,
                EnableNotification = entry.EnableNotification,
                HideWhenNoAdsOwned = entry.HideWhenNoAdsOwned
            });
        }

        normalized = list.ToArray();
        return true;
    }
}

internal sealed class GameplayBalanceTitleDataModel
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
    public FeatureGateTitleDataModel[]? FeatureGates { get; set; }
    public int[]? SizeXp { get; set; }
    public float[]? HoleScaleMultipliers { get; set; }
    public float[]? HoleSpeedMultipliers { get; set; }
}

internal sealed class FeatureGateTitleDataModel
{
    public string FeatureId { get; set; } = string.Empty;
    public int? UnlockLevel { get; set; }
    public bool? EnableNotification { get; set; }
    public bool? HideWhenNoAdsOwned { get; set; }
}
