namespace Serhat.Forge.CloudScript.Domain.Validation;

/// <summary>
/// Result of a validation operation.
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid { get; }
    public List<ValidationError> Errors { get; }

    private ValidationResult(bool isValid, List<ValidationError>? errors = null)
    {
        IsValid = isValid;
        Errors = errors ?? new List<ValidationError>();
    }

    public static ValidationResult Success() => new(true);

    public static ValidationResult Failure(params ValidationError[] errors) =>
        new(false, errors.ToList());

    public static ValidationResult Failure(string field, string message) =>
        new(false, new List<ValidationError> { new(field, message) });

    public Dictionary<string, string> ToDetailsDictionary() =>
        Errors.ToDictionary(e => e.Field, e => e.Message);
}

/// <summary>
/// A single validation error.
/// </summary>
public sealed class ValidationError
{
    public string Field { get; }
    public string Message { get; }

    public ValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }
}

/// <summary>
/// Validator for request DTOs.
/// </summary>
public static class RequestValidator
{
    public static ValidationResult ValidateSubmitLevelResult(DTOs.SubmitLevelResultRequestDto? request)
    {
        if (request == null)
            return ValidationResult.Failure("request", "Request cannot be null");

        var errors = new List<ValidationError>();

        if (request.LevelId < 1)
            errors.Add(new("LevelId", "Must be >= 1"));

        if (request.TimeSec < 0)
            errors.Add(new("TimeSec", "Must be non-negative"));

        if (request.Stars < 1 || request.Stars > 3)
            errors.Add(new("Stars", "Must be between 1 and 3"));

        if (request.CrownsCollected < 0)
            errors.Add(new("CrownsCollected", "Must be non-negative"));

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors.ToArray());
    }

    public static ValidationResult ValidateGetLeaderboard(DTOs.GetLeaderboardRequestDto? request)
    {
        if (request == null)
            return ValidationResult.Failure("request", "Request cannot be null");

        var errors = new List<ValidationError>();

        if (!string.Equals(request.Scope, DTOs.LeaderboardScopes.World, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Scope, DTOs.LeaderboardScopes.Country, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ValidationError("Scope", $"Must be '{DTOs.LeaderboardScopes.World}' or '{DTOs.LeaderboardScopes.Country}'"));
        }

        if (request.PageSize < 1 || request.PageSize > 1000)
        {
            errors.Add(new ValidationError("PageSize", "Must be between 1 and 1000"));
        }

        if (request.StartingPosition < 1)
        {
            errors.Add(new ValidationError("StartingPosition", "Must be >= 1"));
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors.ToArray());
    }
}
