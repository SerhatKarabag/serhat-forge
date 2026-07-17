using Serhat.Forge.CloudScript.Infrastructure.Idempotency;
using Xunit;

namespace Serhat.Forge.CloudScript.Tests;

public class IdempotencyStoreTests
{
    private readonly InMemoryIdempotencyStore _store = new();

    [Fact]
    public async Task TryGet_NonExistent_ReturnsNotFound()
    {
        var result = await _store.TryGetAsync("player", "func", "key");

        Assert.Equal(IdempotencyStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task TryBegin_NewKey_Succeeds()
    {
        var result = await _store.TryBeginAsync("player", "func", "key");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task TryBegin_DuplicateKey_ReturnsFalseWithExistingStatus()
    {
        await _store.TryBeginAsync("player", "func", "key");

        var result = await _store.TryBeginAsync("player", "func", "key");

        Assert.False(result.Success);
        Assert.Equal(IdempotencyStatus.InProgress, result.ExistingStatus);
    }

    [Fact]
    public async Task Complete_StoresResponse()
    {
        await _store.TryBeginAsync("player", "func", "key");

        await _store.CompleteAsync("player", "func", "key", "{\"success\":true}");

        var result = await _store.TryGetAsync("player", "func", "key");

        Assert.Equal(IdempotencyStatus.Completed, result.Status);
        Assert.Equal("{\"success\":true}", result.ResponsePayload);
    }

    [Fact]
    public async Task Fail_StoresError()
    {
        await _store.TryBeginAsync("player", "func", "key");

        await _store.FailAsync("player", "func", "key", "ERROR_CODE", "Error message");

        var result = await _store.TryGetAsync("player", "func", "key");

        Assert.Equal(IdempotencyStatus.Failed, result.Status);
        Assert.Equal("ERROR_CODE", result.ErrorCode);
        Assert.Equal("Error message", result.ErrorMessage);
    }

    [Fact]
    public async Task TryBegin_AfterComplete_ReturnsCachedResponse()
    {
        await _store.TryBeginAsync("player", "func", "key");
        await _store.CompleteAsync("player", "func", "key", "{\"data\":123}");

        var result = await _store.TryBeginAsync("player", "func", "key");

        Assert.False(result.Success);
        Assert.Equal(IdempotencyStatus.Completed, result.ExistingStatus);
        Assert.Equal("{\"data\":123}", result.ExistingResponsePayload);
    }

    [Fact]
    public async Task DifferentKeys_AreIndependent()
    {
        await _store.TryBeginAsync("player", "func", "key1");
        await _store.CompleteAsync("player", "func", "key1", "response1");

        var result = await _store.TryBeginAsync("player", "func", "key2");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task DifferentPlayers_AreIndependent()
    {
        await _store.TryBeginAsync("player1", "func", "key");
        await _store.CompleteAsync("player1", "func", "key", "response1");

        var result = await _store.TryBeginAsync("player2", "func", "key");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task DifferentFunctions_AreIndependent()
    {
        await _store.TryBeginAsync("player", "func1", "key");
        await _store.CompleteAsync("player", "func1", "key", "response1");

        var result = await _store.TryBeginAsync("player", "func2", "key");

        Assert.True(result.Success);
    }
}
