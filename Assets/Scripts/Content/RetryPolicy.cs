using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Forge.Content
{
    /// <summary>
    /// SRP: Handles retry logic and timeout management for async operations.
    /// Separated from GameBootstrapper to encapsulate retry/timeout behavior.
    /// </summary>
    public sealed class RetryPolicy
    {
        private readonly TimeSpan _timeout;
        private readonly int _retryAttempts;
        private readonly TimeSpan _retryDelay;
        private readonly bool _verboseLogging;

        /// <summary>
        /// Creates a new retry policy with the specified settings.
        /// </summary>
        /// <param name="timeout">Timeout for each attempt.</param>
        /// <param name="retryAttempts">Number of retry attempts after first failure.</param>
        /// <param name="retryDelay">Delay between retry attempts.</param>
        /// <param name="verboseLogging">Whether to log retry attempts.</param>
        public RetryPolicy(TimeSpan timeout, int retryAttempts, TimeSpan retryDelay, bool verboseLogging = false)
        {
            _timeout = timeout;
            _retryAttempts = retryAttempts;
            _retryDelay = retryDelay;
            _verboseLogging = verboseLogging;
        }

        /// <summary>
        /// Creates a retry policy from ContentConfiguration.
        /// </summary>
        public static RetryPolicy FromConfiguration(ContentConfiguration config)
        {
            return new RetryPolicy(
                config.NetworkTimeout,
                config.RetryAttempts,
                config.RetryDelay,
                config.VerboseLogging
            );
        }

        /// <summary>
        /// Executes an operation with timeout and retry logic.
        /// </summary>
        /// <param name="operation">The async operation to execute.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The result of the operation.</returns>
        public async Task<ContentOperationResult> ExecuteAsync(
            Func<CancellationToken, Task<ContentOperationResult>> operation,
            CancellationToken ct)
        {
            var attempts = 0;
            ContentOperationResult lastResult = ContentOperationResult.Failure(ContentLoadStatus.Error, "No attempts made.");

            while (attempts <= _retryAttempts && !ct.IsCancellationRequested)
            {
                attempts++;

                using var timeoutCts = new CancellationTokenSource(_timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                try
                {
                    var task = operation(linkedCts.Token);
                    var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token));

                    if (completed == task)
                    {
                        lastResult = await task;

                        if (lastResult.IsSuccess)
                        {
                            return lastResult;
                        }
                    }
                    else
                    {
                        if (ct.IsCancellationRequested)
                        {
                            return ContentOperationResult.Failure(ContentLoadStatus.Cancelled, "Operation cancelled.");
                        }

                        lastResult = ContentOperationResult.Failure(ContentLoadStatus.Timeout, "Operation timed out.");
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    lastResult = ContentOperationResult.Failure(ContentLoadStatus.Timeout, "Operation timed out.");
                }
                catch (OperationCanceledException)
                {
                    return ContentOperationResult.Failure(ContentLoadStatus.Cancelled, "Operation cancelled.");
                }
                catch (Exception ex)
                {
                    lastResult = ContentOperationResult.FromException(ex);
                }

                if (attempts <= _retryAttempts && !ct.IsCancellationRequested)
                {
                    Log($"Retry {attempts}/{_retryAttempts} after failure: {lastResult.ErrorMessage}");
                    await Task.Delay(_retryDelay, ct);
                }
            }

            return lastResult;
        }

        /// <summary>
        /// Executes a simple async operation with timeout and retry logic.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="operation">The async operation to execute.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The result of the operation, or default if failed.</returns>
        public async Task<(bool success, T result)> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct)
        {
            var attempts = 0;
            Exception lastException = null;

            while (attempts <= _retryAttempts && !ct.IsCancellationRequested)
            {
                attempts++;

                using var timeoutCts = new CancellationTokenSource(_timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                try
                {
                    var task = operation(linkedCts.Token);
                    var completedTask = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token));

                    if (completedTask == task)
                    {
                        var result = await task;
                        return (true, result);
                    }

                    if (ct.IsCancellationRequested)
                    {
                        return (false, default);
                    }

                    lastException = new TimeoutException("Operation timed out.");
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    lastException = new TimeoutException("Operation timed out.");
                }
                catch (OperationCanceledException)
                {
                    return (false, default);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }

                if (attempts <= _retryAttempts && !ct.IsCancellationRequested)
                {
                    Log($"Retry {attempts}/{_retryAttempts} after failure: {lastException?.Message}");
                    await Task.Delay(_retryDelay, ct);
                }
            }

            return (false, default);
        }

        private void Log(string message)
        {
            if (_verboseLogging)
            {
                Debug.LogWarning($"[RetryPolicy] {message}");
            }
        }
    }
}
