# Serhat Forge Monetization CloudScript

Separate .NET 8 Azure Functions module for store verification, entitlement grants,
and subscription lifecycle processing.

## Security guarantees

- Apple purchase and App Store Server Notification JWS certificate-chain/signature validation
- Google Play purchase verification and RTDN OIDC audience/service-account validation
- PlayFab ExecuteFunction identity binding to the trusted title-player lineage
- Atomic purchase/webhook deduplication and idempotent entitlement processing
- Product allowlisting and application/environment identity checks
- Production startup validation that rejects fake verifiers, signature bypasses,
  missing secrets, development storage, and unsafe sandbox configuration

Unknown environment names are treated as production and fail closed. Raw request
envelopes and fake store verification are accepted only in Development/Local/Test.

## Functions

- `VerifyPurchase` - verifies a store purchase before granting entitlements
- `GetEntitlements` - returns the authenticated player's current entitlements
- `AppleNotifications` - App Store Server Notifications v2 (`/api/webhooks/apple`)
- `GoogleRtdn` - Google Real-time Developer Notifications (`/api/webhooks/google`)

The old `IapVerify`, `IapGetEntitlements`, and `GrantPurchaseRewards` endpoints in
`Samples~/GameApiBackend` are retired and always return
`410 Gone / LEGACY_MONETIZATION_DISABLED`.

## Local development

1. Copy `local.settings.template.json` to `local.settings.json`.
2. Keep `AZURE_FUNCTIONS_ENVIRONMENT=Development`.
3. Start Azurite if Table Storage is required.
4. Set `USE_FAKE_VERIFIER=true` only for local fake-receipt flows.
5. Run:

```bash
func start
```

Startup rejects `USE_FAKE_VERIFIER=true` outside Development/Local/Test.

## Production configuration

Deploy this project to a separate Function App and configure at least:

- `PLAYFAB_TITLE_ID`, `PLAYFAB_DEV_SECRET_KEY`
- `MONETIZATION_STORAGE_CONNECTION`
- `USE_FAKE_VERIFIER=false`
- `APPLE_BUNDLE_ID`, `APPLE_APP_ID`, `APPLE_ISSUER_ID`, `APPLE_KEY_ID`
- `APPLE_PRIVATE_KEY_BASE64`, `APPLE_ROOT_CA_BASE64`
- `GOOGLE_PACKAGE_NAME`, `GOOGLE_SERVICE_ACCOUNT_EMAIL`, `GOOGLE_PRIVATE_KEY_BASE64`
- `GOOGLE_PUBSUB_AUDIENCE`, `GOOGLE_PUBSUB_SERVICE_ACCOUNT_EMAIL`
- `ALLOWED_PRODUCTS_JSON`

Never commit real values. Use Function App settings or a secret manager.

Deploy with Azure Functions Core Tools:

```bash
func azure functionapp publish YOUR_MONETIZATION_FUNCTION_APP --dotnet-isolated
```

## Tests

From the repository root:

```bash
dotnet test Samples~/GameApiBackend/tests/Serhat.Forge.CloudScript.Tests.csproj \
  --configuration Release \
  --property:TreatWarningsAsErrors=true
```

The GitHub `Cloud .NET Tests` workflow runs the same release test graph with compiler
warnings treated as errors.
