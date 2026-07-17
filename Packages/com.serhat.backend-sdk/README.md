# Serhat Backend SDK

Transport-agnostic backend foundations for Unity projects. The core package provides explicit building blocks for cloud invocation, resilience, persistent writes, request coalescing, telemetry, serialization, storage, connectivity, and testable time/randomness.

The default runtime contains no game economy, progression, live-service contract, credentials, or production endpoint.

## Package layout

| Assembly | Availability | Purpose |
|---|---|---|
| `Serhat.BackendSdk.Core` | Always available | Provider-neutral contracts and infrastructure under `Serhat.Backend.Core.*` |
| `Serhat.BackendSdk.PlayFab` | Requires `PLAYFAB_SDK` | Optional PlayFab CloudScript transport under `Serhat.Backend.PlayFab` |
| `Serhat.BackendSdk.GameApi` | Imported sample; requires `SERHAT_FORGE_GAME_API_SAMPLE` | Example game-domain DTOs and typed client under `Serhat.Backend.GameApi` |
| `Serhat.BackendSdk.GameApi.Sample` | Imported sample; requires both symbols above | Example PlayFab integration and composition code |

`Serhat.BackendSdk.Core` does not select a provider or own application lifecycle. Supply an `ICloudFunctionInvoker` from your composition root, and own/dispose the infrastructure for the lifetime of your application scope.

## Installation

Serhat Forge embeds this package under `Packages/com.serhat.backend-sdk`.

When extracting it for another Unity project:

1. Copy the complete package directory.
2. Add it as an embedded or local UPM dependency.
3. Retain `LICENSE.md` and the repository's third-party notices.
4. Add provider SDKs and scripting symbols only for adapters you actually use.

The core assembly has no PlayFab dependency.

## Core quick start

Implement `ICloudFunctionInvoker` for your HTTP, platform, or backend SDK transport. Then compose the resilience pipeline once and route operations through it.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.Core.Resilience;
using Serhat.Backend.Core.Telemetry;

public sealed class BackendGateway : IDisposable
{
    private readonly ICloudFunctionInvoker _invoker;
    private readonly ResiliencePipeline _pipeline;

    public BackendGateway(
        ICloudFunctionInvoker invoker,
        ResiliencePipeline pipeline)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public Task<CloudResult<TResponse>> ExecuteReadAsync<TRequest, TResponse>(
        string functionName,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var callOptions = new CloudCallOptions()
            .WithCorrelationId(correlationId);

        return _pipeline.ExecuteReadAsync(
            operationToken => _invoker.ExecuteAsync<TRequest, TResponse>(
                functionName,
                request,
                callOptions,
                operationToken),
            functionName,
            correlationId,
            ct: cancellationToken);
    }

    public Task<CloudResult<TResponse>> ExecuteWriteAsync<TRequest, TResponse>(
        string functionName,
        TRequest request,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var callOptions = new CloudCallOptions()
            .WithCorrelationId(correlationId)
            .WithIdempotencyKey(idempotencyKey);

        return _pipeline.ExecuteWriteAsync(
            operationToken => _invoker.ExecuteAsync<TRequest, TResponse>(
                functionName,
                request,
                callOptions,
                operationToken),
            functionName,
            correlationId,
            ct: cancellationToken);
    }

    public void Dispose() => _pipeline.Dispose();
}
```

Compose the pipeline in your application installer or bootstrap scope:

```csharp
using System;
using Serhat.Backend.Core;
using Serhat.Backend.Core.Resilience;
using Serhat.Backend.Core.Telemetry;

public static class BackendComposition
{
    public static BackendGateway CreateBackendGateway(ICloudFunctionInvoker invoker)
    {
        var options = new BackendSdkOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(20),
            Retry =
            {
                MaxAttempts = 3,
                InitialDelay = TimeSpan.FromMilliseconds(500),
                MaxDelay = TimeSpan.FromSeconds(10)
            },
            CircuitBreaker =
            {
                Enabled = true,
                FailureThreshold = 5,
                OpenDuration = TimeSpan.FromSeconds(30)
            },
            Concurrency =
            {
                MaxConcurrentReads = 8,
                MaxConcurrentWrites = 2
            }
        };

        IClock clock = SystemClock.Instance;
        IRandom random = new SystemRandom();
        IBackendLogger logger = new UnityBackendLogger();
        IBackendTelemetrySink telemetry = new LoggingTelemetrySink(logger);

        var retry = new RetryPolicy(options.Retry, clock, random, logger, telemetry);
        var circuitBreaker = new CircuitBreaker(options.CircuitBreaker, clock, logger);
        var concurrencyLimiter = new ConcurrencyLimiter(options.Concurrency, logger);
        var pipeline = new ResiliencePipeline(
            retry,
            circuitBreaker,
            concurrencyLimiter,
            options,
            logger,
            clock,
            telemetry);

        return new BackendGateway(invoker, pipeline);
    }
}
```

Keep the gateway in an application-lifetime scope. Pass cancellation from the caller, use stable idempotency keys for retryable writes, and dispose the gateway when that scope ends.

## Core components

- `ICloudFunctionInvoker`: provider-neutral asynchronous transport contract.
- `ResiliencePipeline`: concurrency limit, circuit breaker, retry, and timeout composition.
- `PersistentOutbox` and `OutboxFlushWorker`: durable write queue, retry history, and dead-letter handling.
- `RequestCoalescer`: shares one in-flight read across callers using the same key.
- `IBackendTelemetrySink`: request, retry, queue, dead-letter, and circuit-state events.
- `IStorage`, `ISerializer`, `IConnectivity`, `IClock`, and `IRandom`: replaceable boundaries for platform code and deterministic tests.
- `CloudResult<T>` and `BackendError`: expected failure values without exception-driven control flow.

The core exposes these parts independently. Compose only the policies your client needs, or use the optional Game API sample as a more complete wiring reference.

## Result and error handling

Backend operations represent expected transport and service failures as `CloudResult<T>`:

```csharp
var result = await gateway.ExecuteReadAsync<GetProfileRequest, GetProfileResponse>(
    "GetProfile",
    new GetProfileRequest(),
    cancellationToken);

if (result.IsSuccess)
{
    UseProfile(result.Data);
    return;
}

var error = result.Error!;
logger.Warning(
    "Backend call failed. Code={0}, Retryable={1}, CorrelationId={2}",
    error.Code,
    error.Retryable,
    error.CorrelationId);
```

Public core error codes include transport, authentication, validation, conflict, circuit, outbox, serialization, and provider-neutral failure categories. A provider adapter may map its native error into `BackendError.ProviderErrorCode` while keeping application code independent of that provider.

Programmer errors such as invalid constructor arguments or use-after-dispose can still throw. Cancellation and timeout behavior depends on both the resilience layer and the transport honoring the supplied `CancellationToken`.

## Persistent outbox guidance

Use the outbox only for commands that are safe to retry and can be serialized durably.

- Assign a stable idempotency key before the first send attempt.
- Never enqueue secrets, session tickets, or unnecessary personal data.
- Define queue-size, retry, dead-letter, retention, and observability policies for your game.
- Call `LoadAsync` before accepting writes and coordinate `OutboxFlushWorker.StopAsync` during shutdown.
- Treat local persistence as recoverability, not proof that the server accepted a command.

`GameApiClientBuilder` in the optional sample demonstrates complete outbox and flush-worker composition.

## Optional PlayFab adapter

`Runtime/PlayFab` is excluded from compilation unless both conditions are true:

1. A compatible PlayFab Unity SDK is installed.
2. `PLAYFAB_SDK` is defined for the target.

The adapter provides `PlayFabCloudFunctionInvoker`. Construct it with your validated `BackendSdkOptions`, serializer, logger, clock, and optional player-ID provider, then inject it as `ICloudFunctionInvoker`.

The package does not include a PlayFab title ID, developer secret, session ticket, or environment binding. Authentication and player-session ownership remain responsibilities of the downstream game.

## Optional Game API reference sample

The **Game API Reference** Package Manager sample demonstrates one possible typed client over the core SDK. It intentionally contains game-specific concepts such as levels, lives, currencies, boosters, daily rewards, leaderboards, and progression.

To use it:

1. Import **Game API Reference** from Package Manager Samples.
2. Add `SERHAT_FORGE_GAME_API_SAMPLE` to the target's scripting define symbols.
3. Implement or select an `ICloudFunctionInvoker`.
4. If using the included PlayFab integration, install PlayFab and also define `PLAYFAB_SDK`.
5. Start with the imported sample's `README.md` and `GameApiSample` composition example.

`Serhat.BackendSdk.GameApi` is not part of the default runtime contract. Replace its DTOs and function names with your own domain rather than depending on them from reusable core code.

## Migration from the legacy combined client

Version 2 separates provider-neutral infrastructure from provider and game-domain code.

- Replace legacy core dependencies on `IBackendClient` with your own domain client over `ICloudFunctionInvoker`.
- Use `Serhat.Backend.Core.*` for resilience, persistence, coalescing, and telemetry.
- Enable `Serhat.Backend.PlayFab` only when PlayFab is installed.
- Import `Serhat.Backend.GameApi` only as a reference sample or migration aid.
- `BackendError.ProviderErrorCode` replaces provider-specific error fields in core error handling.

## Monetization

Monetization is delivered separately as `com.serhat.monetization-sdk`. It adds Unity IAP, purchase verification contracts, subscription policy, and entitlement flows; none of those are required by this core package.

## Requirements and release validation

- Unity `6000.3` or newer, matching `package.json`.
- A project-compatible implementation of `ICloudFunctionInvoker`.
- Provider SDKs only for optional adapters you enable.

The core code is designed for Unity's managed runtime and avoids runtime code generation. AOT/IL2CPP compatibility must still be validated by each downstream project with its selected provider SDKs, link-preservation rules, target platforms, and stripping level. A clean Android/iOS or console IL2CPP build is a release gate, not a guarantee provided by this package.

## License

First-party package code is licensed under the included `LICENSE.md`. Provider SDKs retain their own licenses.
