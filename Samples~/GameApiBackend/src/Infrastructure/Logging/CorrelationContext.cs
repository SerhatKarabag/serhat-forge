namespace Serhat.Forge.CloudScript.Infrastructure.Logging;

/// <summary>
/// Interface for correlation context.
/// </summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
    void SetCorrelationId(string correlationId);
}

/// <summary>
/// Thread-safe correlation context using AsyncLocal.
/// </summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<string> _correlationId = new();

    public string CorrelationId => _correlationId.Value ?? string.Empty;

    public void SetCorrelationId(string correlationId)
    {
        _correlationId.Value = correlationId;
    }
}
