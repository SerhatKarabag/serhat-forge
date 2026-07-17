# Game API Reference sample

Optional Unity client reference showing how to layer strongly typed, game-specific contracts over the transport-agnostic Serhat Backend SDK.

The domain includes levels, progress, lives, coins, boosters, daily rewards, and leaderboards. These types belong to the sample game. Replace them with your own game contracts instead of moving them into the core SDK.

## Included pieces

- `IGameApiClient` and `GameApiClient`
- Typed request/response DTOs
- Resilience pipeline for reads/writes
- Request coalescing for selected reads
- Idempotency keys for writes
- Persistent outbox fallback for retryable write failures
- `GameApiClientBuilder`
- `GameApiSample` standalone usage component
- `BackendManager` Serhat Forge composition example

The matching optional server reference is in `Samples~/GameApiBackend` at the repository root.

## Enable

1. In Package Manager, import **Game API Reference** from the Serhat Backend SDK samples.
2. Install and configure the PlayFab Unity SDK if using the included transport.
3. Add `PLAYFAB_SDK` to scripting define symbols after the PlayFab assemblies are available.
4. Add `SERHAT_FORGE_GAME_API_SAMPLE` to scripting define symbols for the targets that should compile this sample.
5. Wait for Unity to recompile and confirm these assemblies load without errors:
   - `Serhat.BackendSdk.GameApi`
   - `Serhat.BackendSdk.GameApi.Sample`
6. Deploy/register the matching Game API functions before testing real calls.

The sample is excluded from compilation until its define is enabled. It contains no PlayFab developer secret, session ticket, Function key, or production endpoint.

## Required lifecycle

Authenticate the player with PlayFab first. `PlayFabCloudFunctionInvoker` uses the PlayFab SDK's current authenticated state and ExecuteFunction API; it does not perform login.

Then build one client for the signed-in application/session scope:

```csharp
using System;
using System.Threading;
using Serhat.Backend.Core;
using Serhat.Backend.GameApi;
using Serhat.Backend.PlayFab;

public static async System.Threading.Tasks.Task<IGameApiClient> CreateGameApiAsync(
    string titleId,
    CancellationToken cancellationToken)
{
    var serializer = new PlayFabSimpleJsonSerializer();
    var clock = SystemClock.Instance;
    var logger = new UnityBackendLogger("GameApi");

    var transportOptions = new BackendSdkOptions
    {
        TitleId = titleId,
        Environment = "production",
        DefaultTimeout = TimeSpan.FromSeconds(15)
    };
    transportOptions.Retry.MaxAttempts = 2;

    var invoker = new PlayFabCloudFunctionInvoker(
        transportOptions,
        serializer,
        logger,
        clock);

    return await GameApiClientBuilder.Create()
        .WithTitleId(titleId)
        .WithEnvironment("production")
        .WithSerializer(serializer)
        .WithClock(clock)
        .WithLogger(logger)
        .WithInvoker(invoker)
        .WithOptions(options =>
        {
            options.DefaultTimeout = TimeSpan.FromSeconds(15);
            options.Retry.MaxAttempts = 2;
            options.Outbox.Enabled = true;
            options.Outbox.AutoStartFlushWorker = true;
        })
        .BuildAsync(cancellationToken);
}
```

Cancel in-flight calls and dispose `IGameApiClient` when its owner is destroyed or the application replaces the client. After logout, do not issue calls until the next PlayFab authentication and a deliberate client lifecycle decision.

`BackendManager.InitializeAsync(cancellationToken)` demonstrates scene-side composition. Adapt it to your DI/application root; do not copy singleton lifecycle assumptions blindly.

## Calling the API

Read bootstrap data after authentication:

```csharp
var result = await client.GetBootstrapAsync(cancellationToken);
result.Match(
    onSuccess: bootstrap => ApplyAuthoritativeProgress(bootstrap.Progress),
    onFailure: error => ShowRetryOrOfflineState(error));
```

Use a stable idempotency key when a UI intent may be retried by your own code:

```csharp
var writeOptions = new WriteOptions
{
    IdempotencyKey = operationId,
    AllowOutboxFallback = true,
    OutboxPriority = 5
};

var result = await client.SubmitLevelResultAsync(
    new SubmitLevelResultRequestDto
    {
        LevelId = levelId,
        Stars = stars,
        TimeSec = durationSeconds
    },
    writeOptions,
    cancellationToken);
```

Reuse `operationId` only for retries of the same completed-level intent. Generate a new GUID for a new completion.

## Outbox semantics

When a write fails with a retryable error and outbox fallback is enabled, the command can be persisted for later delivery. The current method still returns its failure; being queued is not the same as being server-confirmed.

- Do not grant currency, rewards, or progression optimistically from an outbox enqueue.
- Reconcile from a later successful response or `GetBootstrapAsync`.
- Surface an honest pending/offline state to the player.
- Monitor `GetOutboxStatus()` for pending and dead-letter commands.
- Call `FlushOutboxAsync` after connectivity returns or before orderly teardown when time allows.
- Dispose the client so its background worker and resources are released.

## Security boundary

- The PlayFab Title ID may be present in the client; the PlayFab developer secret and Azure Function keys must never be.
- Production server functions ignore client-supplied player identity and bind to the trusted PlayFab ExecuteFunction wrapper.
- Keep request DTOs free of credentials and sensitive receipt data.
- Treat all client values as untrusted input on the server.
- Do not log access tokens, session tickets, secrets, or full personal identifiers.

## Disabled monetization API

`IGameApiClient.GrantPurchaseRewardsAsync` remains in this preview sample only for migration compatibility. The matching gameplay Function returns `410 Gone` and never grants anything. Do not call it.

Use `com.serhat.monetization-sdk` with the separate hardened backend functions `VerifyPurchase` and `GetEntitlements` for store purchases.

## Before shipping

- [ ] Replace every sample DTO/rule with the game's reviewed contract.
- [ ] Authenticate before creating/calling the client.
- [ ] Deploy and register only intended client-callable functions.
- [ ] Test retry, timeout, circuit-breaker, offline queue, duplicate-write, and dead-letter behavior.
- [ ] Reconcile outbox-delivered writes from authoritative server state.
- [ ] Dispose clients and cancel operations during logout/teardown.
- [ ] Keep all server credentials out of the Unity project.
