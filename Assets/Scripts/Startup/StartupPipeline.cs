using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Forge.Startup
{
    public readonly struct StartupPipelineResult
    {
        private StartupPipelineResult(bool succeeded, StartupStep failedStep, Exception error)
        {
            Succeeded = succeeded;
            FailedStep = failedStep;
            Error = error;
        }

        public bool Succeeded { get; }
        public StartupStep FailedStep { get; }
        public Exception Error { get; }

        public static StartupPipelineResult Success() => new StartupPipelineResult(true, null, null);

        public static StartupPipelineResult Failure(StartupStep step, Exception error) =>
            new StartupPipelineResult(false, step, error);
    }

    /// <summary>
    /// Runs startup steps sequentially with timeout, retry and required/optional semantics.
    /// Timeout is terminal because retrying a partially completed side effect is unsafe.
    /// </summary>
    public sealed class StartupPipeline
    {
        public event Action<int, int, StartupStep> StepStarted;
        public event Action<int, int, StartupStep> StepCompleted;

        public async Task<StartupPipelineResult> RunAsync(
            IReadOnlyList<StartupStep> steps,
            CancellationToken cancellationToken = default)
        {
            if (steps == null || steps.Count == 0)
                return StartupPipelineResult.Success();

            for (var index = 0; index < steps.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = steps[index];
                if (step == null)
                {
                    return StartupPipelineResult.Failure(
                        null,
                        new InvalidOperationException($"Startup step at index {index} is missing."));
                }

                if (!step.isActiveAndEnabled)
                {
                    if (step.IsRequired)
                    {
                        return StartupPipelineResult.Failure(
                            step,
                            new InvalidOperationException(
                                $"Required startup step '{step.StepName}' is disabled."));
                    }

                    Debug.LogWarning($"[StartupPipeline] Optional step '{step.StepName}' is disabled; skipping.", step);
                    continue;
                }

                InvokeSafely(StepStarted, index, steps.Count, step);
                var error = await ExecuteWithRetryAsync(step, cancellationToken);
                if (error != null)
                {
                    if (step.IsRequired || error is TimeoutException)
                        return StartupPipelineResult.Failure(step, error);

                    Debug.LogWarning($"[StartupPipeline] Optional step '{step.StepName}' failed: {error.Message}", step);
                }

                InvokeSafely(StepCompleted, index, steps.Count, step);
            }

            return StartupPipelineResult.Success();
        }

        private static async Task<Exception> ExecuteWithRetryAsync(
            StartupStep step,
            CancellationToken cancellationToken)
        {
            Exception lastError = null;
            var attempts = step.RetryCount + 1;

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await ExecuteWithTimeoutAsync(step, cancellationToken);
                    return null;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (TimeoutException exception)
                {
                    // Retrying after an unknown partial side effect is not generally safe.
                    return exception;
                }
                catch (Exception exception)
                {
                    lastError = exception;
                    if (attempt + 1 >= attempts)
                        break;

                    if (step.RetryDelaySeconds > 0f)
                    {
                        await Task.Delay(
                            TimeSpan.FromSeconds(step.RetryDelaySeconds),
                            cancellationToken);
                    }
                }
            }

            return lastError;
        }

        private static async Task ExecuteWithTimeoutAsync(
            StartupStep step,
            CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var operation = step.ExecuteAsync(linkedCts.Token) ?? Task.CompletedTask;
            var cancellationSignal =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellationRegistration = cancellationToken.Register(
                () => cancellationSignal.TrySetResult(true));
            var timeout = step.TimeoutSeconds > 0f
                ? Task.Delay(TimeSpan.FromSeconds(step.TimeoutSeconds))
                : Task.Delay(Timeout.InfiniteTimeSpan);
            var completed = await Task.WhenAny(
                operation,
                timeout,
                cancellationSignal.Task);
            if (completed == operation)
            {
                await operation;
                return;
            }

            linkedCts.Cancel();

            if (step.CancellationGraceSeconds > 0f)
            {
                var cancellationGrace = Task.Delay(
                    TimeSpan.FromSeconds(step.CancellationGraceSeconds));
                var shutdown = await Task.WhenAny(operation, cancellationGrace);
                if (shutdown == operation)
                {
                    await ObserveTimedOutOperationAsync(operation, step, logFailure: false);
                }
                else
                {
                    Debug.LogError(
                        $"[StartupPipeline] Step '{step.StepName}' ignored cancellation for " +
                        $"{step.CancellationGraceSeconds:0.##} seconds. The pipeline is aborted; " +
                        "the step may still be running in the background.",
                        step);
                    _ = ObserveTimedOutOperationAsync(operation, step, logFailure: true);
                }
            }
            else
            {
                _ = ObserveTimedOutOperationAsync(operation, step, logFailure: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            throw new TimeoutException(
                $"Startup step '{step.StepName}' timed out after {step.TimeoutSeconds:0.##} seconds.");
        }

        private static async Task ObserveTimedOutOperationAsync(
            Task operation,
            StartupStep step,
            bool logFailure)
        {
            try
            {
                await operation;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (logFailure)
                    Debug.LogException(exception, step);
            }
        }

        private static void InvokeSafely(
            Action<int, int, StartupStep> handlers,
            int index,
            int count,
            StartupStep step)
        {
            if (handlers == null)
                return;

            foreach (Action<int, int, StartupStep> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(index, count, step);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, step);
                }
            }
        }
    }
}
