# Serhat Forge Game API Backend Sample

> Optional reference implementation. This project demonstrates one possible level/progression economy and is not part of the default Serhat Forge runtime.

Server-side Azure Functions for PlayFab CloudScript with idempotency, validation, and observability.

> **v2.0 Changes**: Namespace standardized as `Serhat.Forge.CloudScript`. TitleId is now injected via configuration rather than hardcoded.

## Features

- **Idempotent Operations**: Prevents duplicate writes via idempotency store
- **Server-Authoritative**: All game logic validated server-side
- **Structured Logging**: Correlation IDs and telemetry
- **PlayFab Integration**: Server API for secure data access
- **Validation**: Input validation with detailed error messages
- **Anti-Cheat**: Server-side integrity checks

## Project Structure

```
Samples~/GameApiBackend/
  src/
    Functions/           # Azure Function endpoints
      FunctionBase.cs
      GetBootstrapFunction.cs
      SubmitLevelResultFunction.cs
      SyncPlayerStateFunction.cs
      GrantPurchaseRewardsFunction.cs # retired; always returns HTTP 410
      IapVerifyFunction.cs             # retired; always returns HTTP 410
      IapGetEntitlementsFunction.cs    # retired; always returns HTTP 410
    Domain/
      DTOs/              # Data transfer objects
      Validation/        # Request validators
      ErrorCodes.cs
      PlayerProgressMerger.cs
    Infrastructure/
      Idempotency/       # Idempotency store
      PlayFab/           # PlayFab server gateway
      Logging/           # Correlation context
  tests/
    Unit/                # xUnit tests
  Program.cs
  host.json
  local.settings.template.json
```

> The legacy monetization endpoints are intentionally fail-closed and always return
> `410 Gone / LEGACY_MONETIZATION_DISABLED`. Deploy and register the separate hardened
> `cloudscript-azure-functions-monetization` Function App for all purchase flows.

## Prerequisites

- .NET 8 SDK
- Azure Functions Core Tools v4
- Azure Storage Emulator (Azurite) for local development
- PlayFab account with Title ID and Developer Secret Key

## Local Development

### 1. Configure Settings

Copy `local.settings.template.json` to `local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "PLAYFAB_TITLE_ID": "YOUR_TITLE_ID",
    "PLAYFAB_DEV_SECRET_KEY": "YOUR_SECRET_KEY",
    "AZURE_STORAGE_CONNECTION_STRING": "UseDevelopmentStorage=true",
    "IDEMPOTENCY_TABLE_NAME": "IdempotencyStore",
    "IDEMPOTENCY_TTL_HOURS": "24"
  }
}
```

### 2. Start Storage Emulator

```bash
# Using Azurite
azurite --silent --location ./azurite-data --debug ./azurite-debug.log
```

### 3. Run the Functions

```bash
cd Samples~/GameApiBackend
func start
```

Functions will be available at `http://localhost:7071/api/`

## API Endpoints

### GetBootstrap

Loads player progress.

**Request:**
```json
{
  "functionName": "GetBootstrap",
  "correlationId": "abc12345",
  "payload": {},
  "caller": {
    "playerId": "PLAYER_ID",
    "titleId": "YOUR_TITLE"
  }
}
```

**Response:**
```json
{
  "correlationId": "abc12345",
  "success": true,
  "data": {
    "progress": {
      "schemaVersion": 1,
      "stateVersion": 1,
      "playerId": "PLAYER_ID",
      "currentLevel": 1,
      "results": {},
      "lastUpdatedUtc": "2026-01-20T14:00:00Z"
    }
  },
  "processingTimeMs": 45
}
```

### SubmitLevelResult

Submits a completed level result. **Requires idempotencyKey**.

**Request:**
```json
{
  "functionName": "SubmitLevelResult",
  "correlationId": "def67890",
  "idempotencyKey": "unique-guid-here",
  "payload": {
    "levelId": 1,
    "stars": 3,
    "timeSec": 54.2
  },
  "caller": {
    "playerId": "PLAYER_ID"
  }
}
```

**Response:**
```json
{
  "correlationId": "def67890",
  "success": true,
  "data": {
    "success": true,
    "newCurrentLevel": 2
  }
}
```

### SyncPlayerState

Synchronizes mutable player state. Consumption changes are accepted, gains remain server-authoritative.

### Legacy monetization endpoints

`GrantPurchaseRewards`, `IapVerify`, and `IapGetEntitlements` are retained only to
fail closed during migration. They never verify, grant, or return purchase data.

## Idempotency

All write operations require an `idempotencyKey` in the request envelope:

1. First request with key: Operation executed and result cached
2. Duplicate request with same key: Cached result returned
3. Keys expire after configured TTL (default: 24 hours)

### Storage

- **Production**: Azure Table Storage
- **Local Dev**: In-memory store (or Table Storage with Azurite)

### Table Schema

| Column | Description |
|--------|-------------|
| PartitionKey | `{TitleId}:{FunctionName}` |
| RowKey | `{PlayerId}:{IdempotencyKey}` |
| Status | `InProgress`, `Completed`, `Failed` |
| ResponsePayload | JSON response (for completed) |
| CreatedAtUtc | Creation timestamp |
| ExpiresAtUtc | TTL expiration |

### Cleanup

Expired records should be cleaned up via:
- Azure Table Storage TTL policy (if available)
- Scheduled Azure Function (recommended)
- Manual cleanup job

## Error Handling

All errors follow this format:

```json
{
  "correlationId": "...",
  "success": false,
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "Invalid request",
    "retryable": false,
    "details": {
      "LevelId": "LevelId must be >= 1"
    }
  }
}
```

### Error Codes

| Code | Retryable | Description |
|------|-----------|-------------|
| `VALIDATION_FAILED` | No | Request validation failed |
| `MISSING_IDEMPOTENCY_KEY` | No | Write operation without idempotency key |
| `INVALID_LEVEL` | No | Level id is invalid or out of sequence |
| `ALREADY_COMPLETED` | No | Level result already recorded |
| `IDEMPOTENCY_CONFLICT` | Yes | Request already in progress |
| `PLAYFAB_ERROR` | Varies | PlayFab API error |
| `INTERNAL_ERROR` | Yes | Unexpected server error |


## Testing

```bash
dotnet test Samples~/GameApiBackend/tests/Serhat.Forge.CloudScript.Tests.csproj \
  --configuration Release \
  --property:TreatWarningsAsErrors=true
```

Tests include:
- `PlayerProgressMergerTests` - Merge logic
- `IdempotencyStoreTests` - In-memory store behavior
- `ValidationTests` - Request validation


## Deployment

### Azure Target Template

| Field | Value |
|---|---|
| Azure account | `<AZURE_ACCOUNT>` |
| Subscription | `<AZURE_SUBSCRIPTION_NAME>` (`<AZURE_SUBSCRIPTION_ID>`) |
| Resource Group | `<AZURE_RESOURCE_GROUP>` |
| Function App | `<FUNCTION_APP_NAME>` |
| Default host | `<FUNCTION_APP_NAME>.azurewebsites.net` |
| Runtime | Linux, .NET 8 isolated |

**Single deploy command** (run from repo root or this folder):

```bash
cd Samples~/GameApiBackend
func azure functionapp publish <FUNCTION_APP_NAME>
```

`func azure functionapp publish` handles the .NET-isolated packaging (including the hidden `.azurefunctions/` folder) automatically — always use this instead of hand-zipping.

**Pre-flight checks** (a matter of seconds, catches 90% of deploy failures):

```bash
# 1. Logged into the right Azure account?
az account show --query "{user:user.name, sub:name}" -o table
#    Expect: <AZURE_ACCOUNT> / <AZURE_SUBSCRIPTION_NAME>

# 2. Function App is running?
az functionapp show -g <AZURE_RESOURCE_GROUP> -n <FUNCTION_APP_NAME> --query state -o tsv
#    Expect: Running

# 3. Code builds clean?
dotnet build --nologo -v q
#    Expect: 0 errors

# 4. local.settings.json exists? (git-ignored; required by `func` to detect runtime)
test -f local.settings.json && echo OK || cp local.settings.template.json local.settings.json
```

> **Gotcha**: `func azure functionapp publish` reads `FUNCTIONS_WORKER_RUNTIME` from a local `local.settings.json`, even though the actual runtime is configured on Azure side. If the file is missing you'll see:
> `Can't determine project language from files. Please use one of [--dotnet-isolated, ...]` / `Worker runtime cannot be 'None'`.
> Fix: copy the template (`cp local.settings.template.json local.settings.json`), or create a minimal one with just `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated` and `AzureWebJobsStorage=UseDevelopmentStorage=true`. The file is in `.gitignore` — safe to keep locally.

**Post-deploy verification:**

```bash
# List functions live on the App — new endpoints should appear here.
az functionapp function list -g <AZURE_RESOURCE_GROUP> -n <FUNCTION_APP_NAME> \
    --query "[].name" -o tsv
```

**Known pitfalls (do NOT use these alternatives):**
- Do not zip `publish\*` with a PowerShell wildcard — hidden `.azurefunctions/` is skipped, Azure loads `0 functions`, all HTTP routes return `404`.
- Do not use `az webapp deployment source config-zip` on this project unless you rebuild the zip with `Compress-Archive -Force -Path publish/* -Force` **and** separately add the hidden folder. `func azure functionapp publish` avoids this class of bugs.
- Monetization flows live on a **separate** Function App (deploy `cloudscript-azure-functions-monetization` there) — do NOT point monetization at `<your-gameplay-function-app>`.

**Application Settings that must exist on the Function App** (set once, persist across deploys):
- `PLAYFAB_TITLE_ID`
- `PLAYFAB_DEV_SECRET_KEY` (never commit — server-side only)
- `AZURE_STORAGE_CONNECTION_STRING`
- `IDEMPOTENCY_TABLE_NAME` (default `IdempotencyStore`)
- `IDEMPOTENCY_TTL_HOURS` (default `24`)

### PlayFab Registration (required for every NEW function)

Every newly added `[Function("XYZ")]` attribute on the server needs a matching entry in PlayFab; existing entries keep working across re-deploys because the URL stays the same.

1. Azure Portal → `<your-gameplay-function-app>` → **Functions → `XYZ` → Get Function URL** (function-level key works; you can also use the master host key).
2. PlayFab Game Manager → **Automation → Cloud Script → Functions → Register Function**
   - **Function Name**: `XYZ` (must match the `[Function("XYZ")]` attribute exactly)
   - **Trigger Type**: `HTTP`
   - **Function URL**: the URL from step 1 (includes `?code=...`)
3. Save. Unity client resolves `"XYZ"` via `PlayFab ExecuteFunction` from this point on.

If a client call returns `Function not found`, this registration is the first thing to check.

## Unity SDK Integration

The Unity SDK calls these functions via PlayFab's ExecuteFunction API:

```csharp
// Unity client automatically:
// - Generates correlationId
// - Generates idempotencyKey for writes
// - Wraps payload in RequestEnvelope
// - Handles ResponseEnvelope parsing
```

## Security

- **Never expose `PLAYFAB_DEV_SECRET_KEY`** - Only used server-side
- Production requests require the PlayFab ExecuteFunction wrapper and bind identity
  to `CallerEntityProfile.Lineage.TitlePlayerAccountId`; client-supplied `caller` data
  is ignored. Raw envelopes are accepted only in Development/Local/Test.
- Input validation on all endpoints
- Server-authoritative game logic (level progression validation)
