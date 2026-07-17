namespace Serhat.Forge.CloudScript.Domain.DTOs;

/// <summary>
/// Request envelope from Unity client.
/// </summary>
public sealed class RequestEnvelope<T> where T : class
{
    public string FunctionName { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public T? Payload { get; set; }
    public CallerContext Caller { get; set; } = new();
    public long TimestampMs { get; set; }
    public string SdkVersion { get; set; } = string.Empty;
}

/// <summary>
/// Caller context from Unity client.
/// </summary>
public sealed class CallerContext
{
    public string PlayerId { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? EntityType { get; set; }
    public string TitleId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
}

/// <summary>
/// Response envelope to Unity client.
/// </summary>
public sealed class ResponseEnvelope<T> where T : class
{
    public string CorrelationId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public T? Data { get; set; }
    public ErrorPayload? Error { get; set; }
    public long ProcessingTimeMs { get; set; }

    public static ResponseEnvelope<T> Ok(T data, string correlationId, long processingTimeMs) => new()
    {
        Success = true,
        Data = data,
        CorrelationId = correlationId,
        ProcessingTimeMs = processingTimeMs
    };

    public static ResponseEnvelope<T> Fail(ErrorPayload error, string correlationId, long processingTimeMs) => new()
    {
        Success = false,
        Error = error,
        CorrelationId = correlationId,
        ProcessingTimeMs = processingTimeMs
    };
}

/// <summary>
/// Error payload in response.
/// </summary>
public sealed class ErrorPayload
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool Retryable { get; set; }
    public Dictionary<string, string>? Details { get; set; }

    public static ErrorPayload Create(string code, string message, bool retryable = false,
        Dictionary<string, string>? details = null) => new()
    {
        Code = code,
        Message = message,
        Retryable = retryable,
        Details = details
    };
}
