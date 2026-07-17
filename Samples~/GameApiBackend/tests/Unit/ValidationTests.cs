using Serhat.Forge.CloudScript.Domain.DTOs;
using Serhat.Forge.CloudScript.Domain.Validation;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests;

public class ValidationTests
{
    [Fact]
    public void ValidateSubmitLevelResult_ValidRequest_ReturnsSuccess()
    {
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 3,
            TimeSec = 45.5f,
            CrownsCollected = 2
        };

        var result = RequestValidator.ValidateSubmitLevelResult(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateSubmitLevelResult_NullRequest_ReturnsFailure()
    {
        var result = RequestValidator.ValidateSubmitLevelResult(null);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void ValidateSubmitLevelResult_InvalidStars_ReturnsFailure()
    {
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 5,
            TimeSec = 10f,
            CrownsCollected = 0
        };

        var result = RequestValidator.ValidateSubmitLevelResult(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "Stars");
    }

    [Fact]
    public void ValidateSubmitLevelResult_InvalidLevel_ReturnsFailure()
    {
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 0,
            Stars = 2,
            TimeSec = 10f,
            CrownsCollected = 0
        };

        var result = RequestValidator.ValidateSubmitLevelResult(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "LevelId");
    }

    [Fact]
    public void ValidateSubmitLevelResult_NegativeTime_ReturnsFailure()
    {
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 1,
            TimeSec = -1f,
            CrownsCollected = 0
        };

        var result = RequestValidator.ValidateSubmitLevelResult(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "TimeSec");
    }

    [Fact]
    public void ValidateSubmitLevelResult_NegativeCrownsCollected_ReturnsFailure()
    {
        var request = new SubmitLevelResultRequestDto
        {
            LevelId = 1,
            Stars = 1,
            TimeSec = 12f,
            CrownsCollected = -1
        };

        var result = RequestValidator.ValidateSubmitLevelResult(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "CrownsCollected");
    }

    [Fact]
    public void ValidateGetLeaderboard_ValidRequest_ReturnsSuccess()
    {
        var request = new GetLeaderboardRequestDto
        {
            Scope = LeaderboardScopes.Country,
            PageSize = 1000,
            StartingPosition = 1
        };

        var result = RequestValidator.ValidateGetLeaderboard(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateGetLeaderboard_InvalidScope_ReturnsFailure()
    {
        var request = new GetLeaderboardRequestDto
        {
            Scope = "Regional",
            PageSize = 50,
            StartingPosition = 1
        };

        var result = RequestValidator.ValidateGetLeaderboard(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "Scope");
    }

    [Fact]
    public void ValidateGetLeaderboard_InvalidPagination_ReturnsFailure()
    {
        var request = new GetLeaderboardRequestDto
        {
            Scope = LeaderboardScopes.World,
            PageSize = 1001,
            StartingPosition = 0
        };

        var result = RequestValidator.ValidateGetLeaderboard(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "PageSize");
        Assert.Contains(result.Errors, e => e.Field == "StartingPosition");
    }
}
