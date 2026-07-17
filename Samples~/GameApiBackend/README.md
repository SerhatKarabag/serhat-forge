# Serhat Forge Game API backend sample

Optional .NET 8 Azure Functions reference implementation for a PlayFab-backed game API. It demonstrates trusted caller binding, server-owned progression/economy rules, idempotent writes, structured errors, and observability.

This is a sample game domain, not part of the default Serhat Forge runtime. Replace its levels, lives, currencies, boosters, rewards, leaderboards, and balance rules with your own contracts before shipping.

## What it demonstrates

- PlayFab ExecuteFunction integration
- Production identity binding to `CallerEntityProfile.Lineage.TitlePlayerAccountId`
- Title ID validation against `TitleAuthenticationContext.Id`
- Server-side request and progression validation
- Idempotent write execution backed by Azure Table Storage
- Persistent player progress and PlayFab server operations
- Read/write response envelopes with correlation IDs
- Retryable/non-retryable error classification
- Configurable balance, events, daily gifts, and client-version policy through PlayFab Title Data
- Application Insights integration hooks

This is validation infrastructure, not a complete anti-cheat solution. Add game-specific abuse detection, rate limits, audit trails, and operational alerts appropriate to your threat model.

## Boundary with monetization

The following legacy functions are deliberately disabled and always return `410 Gone / LEGACY_MONETIZATION_DISABLED`:

- `GrantPurchaseRewards`
- `IapVerify`
- `IapGetEntitlements`

Deploy `cloudscript-azure-functions-monetization` to a separate Function App and register its `VerifyPurchase` and `GetEntitlements` functions for every purchase flow. Never grant store value through this gameplay sample.

## Functions

| Function | Type | Purpose |
|---|---|---|
| `GetBootstrap` | Read | Returns authoritative progress, economy/balance configuration, events, and daily-gift state |
| `GetLeaderboard` | Read | Returns world/country leaderboard data |
| `RefreshLeaderboardMetadata` | Idempotent read-style operation | Re-stamps leaderboard metadata from authoritative PlayFab profile/progress data |
| `SubmitLevelResult` | Idempotent write | Validates and applies a completed level result |
| `SyncPlayerState` | Idempotent write | Reconciles allowed mutable state changes; gains remain server-owned |
| `BuyLivesWithCoins` | Idempotent write | Performs an authoritative soft-currency exchange |
| `BuyStartBoosterWithCoins` | Idempotent write | Purchases a configured start-booster offer |
| `BuyBoosterWithCoins` | Idempotent write | Purchases a configured gameplay-booster offer |
| `GrantAdRewardLife` | Idempotent write | Applies the sample rewarded-ad life grant contract |
| `GrantAdRewardCoins` | Idempotent write | Applies the sample rewarded-ad coin grant contract |
| `ClaimRateUsReward` | Idempotent write | Claims a one-time configured reward |
| `ClaimDailyGift` | Idempotent write | Claims the current server-day gift and advances streak state |
| `BackfillLeaderboardPlayer` | Administrative | Backfills one player's name/stats for a controlled PlayFab segment operation |

`BackfillLeaderboardPlayer` intentionally accepts an administrative payload and does not use the normal player identity parser. Protect its function key as an operator credential, do not register it as a client-callable function, and restrict it with your deployment/network policy.

## Prerequisites

- .NET 8 SDK
- Azure Functions Core Tools v4
- Azurite for the default local storage configuration
- A PlayFab title
- A PlayFab developer secret available only to the Function App

## Local development

### 1. Configure local settings

Copy `local.settings.template.json` to the git-ignored `local.settings.json`, then set real development values:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AZURE_FUNCTIONS_ENVIRONMENT": "Development",
    "PLAYFAB_TITLE_ID": "YOUR_DEVELOPMENT_TITLE_ID",
    "PLAYFAB_DEV_SECRET_KEY": "YOUR_LOCAL_DEVELOPMENT_SECRET",
    "AZURE_STORAGE_CONNECTION_STRING": "UseDevelopmentStorage=true",
    "IDEMPOTENCY_TABLE_NAME": "IdempotencyStore",
    "IDEMPOTENCY_TTL_HOURS": "24"
  }
}
```

`PLAYFAB_TITLE_ID` and `PLAYFAB_DEV_SECRET_KEY` are required even locally. Do not reuse production secrets for local development and never commit `local.settings.json`.

### 2. Start storage and functions

```bash
azurite --silent --location ./azurite-data --debug ./azurite-debug.log
```

In another terminal:

```bash
cd Samples~/GameApiBackend
func start
```

The local host exposes routes under `http://localhost:7071/api/`.

Raw inner envelopes are accepted only when the environment is exactly `Development`, `Local`, or `Test`. Production rejects them and requires the PlayFab wrapper.

## Request contract

The Unity Backend SDK creates the inner envelope. A write looks like:

```json
{
  "functionName": "SubmitLevelResult",
  "correlationId": "fb8991b3d3e747f69ca07b8b5bc266bb",
  "idempotencyKey": "550e8400-e29b-41d4-a716-446655440000",
  "payload": {
    "levelId": 1,
    "stars": 3,
    "timeSec": 54.2
  },
  "caller": {
    "playerId": "LOCAL_PLAYER_ONLY",
    "titleId": "YOUR_DEVELOPMENT_TITLE_ID"
  }
}
```

In production, PlayFab wraps this object as `FunctionArgument`/`FunctionParameter`. The Function App ignores the inner caller identity, verifies the wrapper title ID, and replaces caller fields with the trusted title-player account ID.

Do not call Function URLs directly from a shipped Unity client.

## Idempotency

Every operation implemented with `ExecuteIdempotentAsync` requires an `idempotencyKey`:

1. The first request atomically creates an `InProgress` record.
2. A duplicate completed request receives its cached result.
3. A concurrent duplicate receives `IDEMPOTENCY_CONFLICT` and may retry.
4. A failed record returns the stored failure.

Azure Table keys are:

| Field | Value |
|---|---|
| Partition key | `{TitleId}:{FunctionName}` |
| Row key | `{PlayerId}:{IdempotencyKey}` |

Expiration is enforced when records are read; add a scheduled cleanup job to control table growth. `IDEMPOTENCY_TTL_HOURS` must be between 1 and 168.

Keep the same idempotency key when retrying the same user intent. Generate a new key for a genuinely new operation.

## Response contract

Success:

```json
{
  "correlationId": "fb8991b3d3e747f69ca07b8b5bc266bb",
  "success": true,
  "data": {},
  "processingTimeMs": 45,
  "serverUtcNow": "2026-07-17T12:00:00Z"
}
```

Failure:

```json
{
  "correlationId": "fb8991b3d3e747f69ca07b8b5bc266bb",
  "success": false,
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "Invalid request",
    "retryable": false,
    "details": {
      "LevelId": "LevelId must be >= 1"
    }
  },
  "processingTimeMs": 3,
  "serverUtcNow": "2026-07-17T12:00:00Z"
}
```

Only retry failures marked `retryable`, and keep the original idempotency key for that operation.

## PlayFab Title Data

The sample can read these optional keys:

| Key | Purpose |
|---|---|
| `game_balance_v1` | Gameplay/economy balance |
| `crown_event_v1` | Crown event configuration |
| `daily_gift_v1` | Daily-gift schedule |
| `client_version_policy` | Minimum supported client versions |

Invalid or missing optional content follows the behavior implemented by each provider (fallback or warning). Validate Title Data in staging before promoting it.

## Tests

From the repository root:

```bash
dotnet test Samples~/GameApiBackend/tests/Serhat.Forge.CloudScript.Tests.csproj \
  --configuration Release \
  --property:TreatWarningsAsErrors=true
```

The project covers progression merging, request validation, idempotency, monetization hardening/lifecycle code, and webhook parsing. These unit tests do not contact PlayFab, Azure, Apple, or Google; run separate staging integration tests.

## Production deployment

### Required Function App settings

- `AZURE_FUNCTIONS_ENVIRONMENT=Production`
- `PLAYFAB_TITLE_ID`
- `PLAYFAB_DEV_SECRET_KEY`
- `AZURE_STORAGE_CONNECTION_STRING`
- `IDEMPOTENCY_TABLE_NAME` (default `IdempotencyStore`)
- `IDEMPOTENCY_TTL_HOURS` (default `24`)

Production startup rejects missing/placeholder title credentials, missing storage, development storage, invalid table names, and invalid TTL values.

Publish with Azure Functions Core Tools so the .NET isolated worker layout, including `.azurefunctions`, is preserved:

```bash
cd Samples~/GameApiBackend
func azure functionapp publish YOUR_GAMEPLAY_FUNCTION_APP --dotnet-isolated
```

Avoid hand-built deployment ZIPs unless your pipeline explicitly preserves hidden files and validates the deployed function list.

### Register client-callable functions in PlayFab

For each client-callable `[Function("Name")]`:

1. Obtain the function-key URL from the Azure Function App.
2. In PlayFab Game Manager, register an HTTP CloudScript function with the exact same name.
3. Store the URL/key only in the PlayFab registration and deployment system.
4. Do not register `BackfillLeaderboardPlayer` or the three retired monetization stubs as client-callable functions.

After deployment, verify the live function list and exercise authentication, duplicate writes, malformed bodies, oversized bodies, and title-ID mismatch handling in staging.

## Production checklist

- [ ] Sample game contracts and economy values were replaced/reviewed.
- [ ] Unity authenticates with PlayFab before calling the Game API.
- [ ] Secrets and function keys are absent from the client, repository, and logs.
- [ ] Azure Table Storage is persistent and cleanup is scheduled.
- [ ] Client retry/outbox behavior preserves idempotency keys.
- [ ] Server-authoritative rewards are reconciled from server responses/bootstrap, never granted optimistically.
- [ ] Administrative functions have separate access controls.
- [ ] Alerts cover error rate, latency, PlayFab throttling, idempotency conflicts, and failed writes.
- [ ] Monetization is deployed and registered on its separate hardened Function App.
