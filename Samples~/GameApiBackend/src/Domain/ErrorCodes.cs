namespace Serhat.Forge.CloudScript.Domain;

/// <summary>
/// Centralized error codes matching Unity client.
/// </summary>
public static class ErrorCodes
{
    // Transport errors
    public const string NetworkError = "NETWORK_ERROR";
    public const string Timeout = "TIMEOUT";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    public const string RateLimited = "RATE_LIMITED";

    // Auth errors
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string SessionExpired = "SESSION_EXPIRED";

    // Validation errors
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string MissingIdempotencyKey = "MISSING_IDEMPOTENCY_KEY";
    public const string InvalidIdempotencyKey = "INVALID_IDEMPOTENCY_KEY";

    // Business logic errors
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string VersionMismatch = "VERSION_MISMATCH";
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";
    public const string AlreadyClaimed = "ALREADY_CLAIMED";
    public const string InvalidScore = "INVALID_SCORE";
    public const string InvalidLevel = "INVALID_LEVEL";
    public const string AlreadyCompleted = "ALREADY_COMPLETED";
    public const string ProductNotAllowed = "PRODUCT_NOT_ALLOWED";
    public const string VerificationFailed = "VERIFICATION_FAILED";
    public const string AlreadyGranted = "ALREADY_GRANTED";

    // System errors
    public const string InternalError = "INTERNAL_ERROR";
    public const string SerializationError = "SERIALIZATION_ERROR";
    public const string PlayFabError = "PLAYFAB_ERROR";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
}
