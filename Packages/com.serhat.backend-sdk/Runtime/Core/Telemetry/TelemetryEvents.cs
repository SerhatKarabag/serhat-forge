#nullable enable
using System;

namespace Serhat.Backend.Core.Telemetry
{
    /// <summary>
    /// Telemetry sink interface for observability.
    /// </summary>
    public interface IBackendTelemetrySink
    {
        void OnRequestStart(RequestStartEvent evt);
        void OnRequestEnd(RequestEndEvent evt);
        void OnRetry(RetryEvent evt);
        void OnEnqueue(EnqueueEvent evt);
        void OnDequeue(DequeueEvent evt);
        void OnDeadLetter(DeadLetterEvent evt);
        void OnCircuitStateChange(CircuitStateChangeEvent evt);
    }

    /// <summary>
    /// Event fired when a request starts.
    /// </summary>
    public sealed class RequestStartEvent
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public bool IsWrite { get; set; }
        public long TimestampMs { get; set; }
    }

    /// <summary>
    /// Event fired when a request ends.
    /// </summary>
    public sealed class RequestEndEvent
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public bool IsWrite { get; set; }
        public long DurationMs { get; set; }
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
    }

    /// <summary>
    /// Event fired when a retry occurs.
    /// </summary>
    public sealed class RetryEvent
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public int Attempt { get; set; }
        public long DelayMs { get; set; }
        public string? ErrorCode { get; set; }
    }

    /// <summary>
    /// Event fired when a command is enqueued to outbox.
    /// </summary>
    public sealed class EnqueueEvent
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string CommandId { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
        public int QueueSize { get; set; }
    }

    /// <summary>
    /// Event fired when a command is dequeued from outbox.
    /// </summary>
    public sealed class DequeueEvent
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string CommandId { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int AttemptCount { get; set; }
        public int QueueSize { get; set; }
    }

    /// <summary>
    /// Event fired when a command is moved to dead letter.
    /// </summary>
    public sealed class DeadLetterEvent
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string CommandId { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
    }

    /// <summary>
    /// Event fired when circuit breaker state changes.
    /// </summary>
    public sealed class CircuitStateChangeEvent
    {
        public string PreviousState { get; set; } = string.Empty;
        public string NewState { get; set; } = string.Empty;
        public int ConsecutiveFailures { get; set; }
        public long TimestampMs { get; set; }
    }

    /// <summary>
    /// No-op telemetry sink for when telemetry is disabled.
    /// </summary>
    public sealed class NullTelemetrySink : IBackendTelemetrySink
    {
        public static readonly NullTelemetrySink Instance = new();

        public void OnRequestStart(RequestStartEvent evt) { }
        public void OnRequestEnd(RequestEndEvent evt) { }
        public void OnRetry(RetryEvent evt) { }
        public void OnEnqueue(EnqueueEvent evt) { }
        public void OnDequeue(DequeueEvent evt) { }
        public void OnDeadLetter(DeadLetterEvent evt) { }
        public void OnCircuitStateChange(CircuitStateChangeEvent evt) { }
    }

    /// <summary>
    /// Telemetry sink that logs events using IBackendLogger.
    /// </summary>
    public sealed class LoggingTelemetrySink : IBackendTelemetrySink
    {
        private readonly IBackendLogger _logger;

        public LoggingTelemetrySink(IBackendLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void OnRequestStart(RequestStartEvent evt)
        {
            _logger.Debug("[{0}] Request started: {1} (write={2})",
                evt.CorrelationId, evt.OperationName, evt.IsWrite);
        }

        public void OnRequestEnd(RequestEndEvent evt)
        {
            if (evt.Success)
            {
                _logger.Info("[{0}] Request completed: {1} in {2}ms",
                    evt.CorrelationId, evt.OperationName, evt.DurationMs);
            }
            else
            {
                _logger.Warning("[{0}] Request failed: {1} - {2} ({3}ms)",
                    evt.CorrelationId, evt.OperationName, evt.ErrorCode ?? string.Empty, evt.DurationMs);
            }
        }

        public void OnRetry(RetryEvent evt)
        {
            _logger.Debug("[{0}] Retry attempt {1} for {2}, delay {3}ms, error: {4}",
                evt.CorrelationId, evt.Attempt, evt.OperationName, evt.DelayMs, evt.ErrorCode ?? string.Empty);
        }

        public void OnEnqueue(EnqueueEvent evt)
        {
            _logger.Info("[{0}] Enqueued to outbox: {1} (queue size: {2})",
                evt.CorrelationId, evt.FunctionName, evt.QueueSize);
        }

        public void OnDequeue(DequeueEvent evt)
        {
            if (evt.Success)
            {
                _logger.Info("[{0}] Dequeued successfully: {1} (attempts: {2})",
                    evt.CorrelationId, evt.FunctionName, evt.AttemptCount);
            }
            else
            {
                _logger.Warning("[{0}] Dequeue failed: {1} (attempts: {2})",
                    evt.CorrelationId, evt.FunctionName, evt.AttemptCount);
            }
        }

        public void OnDeadLetter(DeadLetterEvent evt)
        {
            _logger.Error("[{0}] Moved to dead letter: {1} - {2} (attempts: {3})", null,
                evt.CorrelationId, evt.FunctionName, evt.Reason, evt.AttemptCount);
        }

        public void OnCircuitStateChange(CircuitStateChangeEvent evt)
        {
            _logger.Warning("Circuit breaker state change: {0} -> {1} (failures: {2})",
                evt.PreviousState, evt.NewState, evt.ConsecutiveFailures);
        }
    }
}
