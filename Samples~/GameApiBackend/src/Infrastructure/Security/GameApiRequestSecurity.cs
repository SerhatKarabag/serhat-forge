using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serhat.Forge.CloudScript.Domain.DTOs;

namespace Serhat.Forge.CloudScript.Infrastructure.GameApiSecurity;

public static class GameApiHttpRequestSecurity
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<string> ReadUtf8BodyAsync(
        Stream body,
        int maxBytes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(8192, maxBytes));
        try
        {
            using var output = new MemoryStream(Math.Min(maxBytes, 16 * 1024));
            while (true)
            {
                var read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > maxBytes)
                {
                    throw new GameApiRequestBodyTooLargeException(maxBytes);
                }

                output.Write(buffer, 0, read);
            }

            return StrictUtf8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("Request body is not valid UTF-8.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

public static class GameApiPlayFabRequestSecurity
{
    public static GameApiPlayFabParseResult<TPayload> ParseEnvelope<TPayload>(
        string requestBody,
        JsonSerializerOptions jsonOptions,
        string expectedTitleId,
        string environmentName,
        string actualFunctionName)
        where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(jsonOptions);

        using var document = JsonDocument.Parse(
            requestBody,
            new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return GameApiPlayFabParseResult<TPayload>.Invalid("INVALID_REQUEST_FORMAT");
        }

        var isPlayFabWrapper =
            TryGetPropertyIgnoreCase(root, "FunctionArgument", out var functionArgument) ||
            TryGetPropertyIgnoreCase(root, "FunctionParameter", out functionArgument);

        string envelopeJson;
        string? trustedPlayerId = null;
        if (isPlayFabWrapper)
        {
            if (!TryResolveTrustedTitlePlayerId(root, out trustedPlayerId) ||
                !TryGetNestedString(root, "TitleAuthenticationContext", "Id", out var contextTitleId) ||
                string.IsNullOrWhiteSpace(expectedTitleId) ||
                !string.Equals(contextTitleId, expectedTitleId, StringComparison.OrdinalIgnoreCase))
            {
                return GameApiPlayFabParseResult<TPayload>.Unauthorized("INVALID_PLAYFAB_CONTEXT");
            }

            envelopeJson = functionArgument.ValueKind == JsonValueKind.String
                ? functionArgument.GetString() ?? string.Empty
                : functionArgument.GetRawText();
        }
        else
        {
            if (!IsDevelopmentEnvironment(environmentName))
            {
                return GameApiPlayFabParseResult<TPayload>.Unauthorized("PLAYFAB_CONTEXT_REQUIRED");
            }

            envelopeJson = requestBody;
        }

        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            return GameApiPlayFabParseResult<TPayload>.Invalid("INVALID_REQUEST_FORMAT");
        }

        var envelope = JsonSerializer.Deserialize<RequestEnvelope<TPayload>>(envelopeJson, jsonOptions);
        if (envelope?.Payload == null)
        {
            return GameApiPlayFabParseResult<TPayload>.Invalid("INVALID_REQUEST_FORMAT");
        }

        envelope.Caller ??= new CallerContext();
        if (isPlayFabWrapper)
        {
            envelope.Caller.PlayerId = trustedPlayerId!;
            envelope.Caller.UserId = trustedPlayerId!;
            envelope.Caller.EntityId = trustedPlayerId;
            envelope.Caller.EntityType = "title_player_account";
            envelope.Caller.TitleId = expectedTitleId;
        }
        else if (string.IsNullOrWhiteSpace(envelope.Caller.PlayerId) &&
                 string.IsNullOrWhiteSpace(envelope.Caller.UserId))
        {
            return GameApiPlayFabParseResult<TPayload>.Unauthorized("PLAYER_ID_REQUIRED");
        }

        envelope.FunctionName = actualFunctionName;
        envelope.CorrelationId = NormalizeCorrelationId(envelope.CorrelationId);
        return GameApiPlayFabParseResult<TPayload>.Success(envelope);
    }

    private static bool TryResolveTrustedTitlePlayerId(JsonElement root, out string playerId)
    {
        playerId = string.Empty;
        if (!TryGetPropertyIgnoreCase(root, "CallerEntityProfile", out var profile) ||
            profile.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryGetNestedString(profile, "Lineage", "TitlePlayerAccountId", out playerId))
        {
            return true;
        }

        if (TryGetPropertyIgnoreCase(profile, "Entity", out var entity) &&
            entity.ValueKind == JsonValueKind.Object &&
            TryGetPropertyIgnoreCase(entity, "Type", out var entityType) &&
            string.Equals(entityType.GetString(), "title_player_account", StringComparison.OrdinalIgnoreCase) &&
            TryGetPropertyIgnoreCase(entity, "Id", out var entityId) &&
            entityId.ValueKind == JsonValueKind.String)
        {
            playerId = entityId.GetString()?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(playerId);
        }

        return false;
    }

    private static bool TryGetNestedString(
        JsonElement root,
        string objectName,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!TryGetPropertyIgnoreCase(root, objectName, out var nested) ||
            nested.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(nested, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool IsDevelopmentEnvironment(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, "Local", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCorrelationId(string? value) =>
        Guid.TryParse(value, out var parsed)
            ? parsed.ToString("N")
            : Guid.NewGuid().ToString("N");
}

public static class GameApiSensitiveLogValue
{
    public static string Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }
}

public sealed class GameApiPlayFabParseResult<TPayload> where TPayload : class
{
    private GameApiPlayFabParseResult(
        RequestEnvelope<TPayload>? envelope,
        bool isUnauthorized,
        string? errorCode)
    {
        Envelope = envelope;
        IsUnauthorized = isUnauthorized;
        ErrorCode = errorCode;
    }

    public RequestEnvelope<TPayload>? Envelope { get; }
    public bool IsSuccess => Envelope != null;
    public bool IsUnauthorized { get; }
    public string? ErrorCode { get; }

    public static GameApiPlayFabParseResult<TPayload> Success(RequestEnvelope<TPayload> envelope) =>
        new(envelope, false, null);

    public static GameApiPlayFabParseResult<TPayload> Invalid(string errorCode) =>
        new(null, false, errorCode);

    public static GameApiPlayFabParseResult<TPayload> Unauthorized(string errorCode) =>
        new(null, true, errorCode);
}

public sealed class GameApiRequestBodyTooLargeException : Exception
{
    public GameApiRequestBodyTooLargeException(int maxBytes)
        : base($"Request body exceeds the {maxBytes}-byte limit.")
    {
        MaxBytes = maxBytes;
    }

    public int MaxBytes { get; }
}