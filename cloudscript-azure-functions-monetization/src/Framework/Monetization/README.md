# Serhat Forge monetization framework

Server-side purchase verification, entitlement granting, transaction persistence, and subscription lifecycle handling for the separate .NET 8 Azure Functions monetization app.

## Trust model

```text
Unity client
  -> PlayFab ExecuteFunction (authenticated player)
    -> VerifyPurchase / GetEntitlements
      -> Apple or Google verification
      -> transaction repository + PlayFab Economy v2

Apple notifications -> signed JWS validation -> refund reconciliation / subscription lifecycle
Google Pub/Sub RTDN -> OIDC validation -> authoritative API reconciliation -> lifecycle
```

The client is untrusted. Production `VerifyPurchase` and `GetEntitlements` requests must arrive in a valid PlayFab ExecuteFunction wrapper. The server replaces client caller data with the trusted title-player identity from `CallerEntityProfile` and verifies the expected PlayFab title ID.

Raw request envelopes are accepted only when `AZURE_FUNCTIONS_ENVIRONMENT` or `DOTNET_ENVIRONMENT` is exactly `Development`, `Local`, or `Test`. Unknown environment names are treated as production.

## Implemented protections

- Apple ES256 JWS signature verification with a three-certificate `x5c` chain, explicit Apple root trust anchors, certificate-profile checks, and configurable online/offline revocation checks
- Google Play one-time and `purchases.subscriptionsv2` receipt verification using a service account
- Google purchase ownership binding to the authenticated PlayFab player through a deterministic obfuscated account ID
- Google Pub/Sub push OIDC validation for exact audience and service-account email
- Apple bundle/app/environment checks and Google package identity configuration
- Product allowlisting before store verification or grants
- Canonical Google token-hash transaction keys, atomic purchase/webhook claims, renewable processing leases, and retryable crash recovery
- Immutable item, quantity, metadata, and tier grant snapshots so catalog changes cannot reinterpret an in-flight paid purchase
- Multi-item subscription grant/revoke snapshots with provider-side effects completed before lifecycle state commits
- PlayFab Economy v2 grants performed only after verification
- PlayFab title-entity-token exchange, complete continuation-token inventory pagination,
  stack quantities/expiry, and explicit provider-failure results
- Transaction-scoped refund/revocation idempotency; ambiguous partial or non-unit refunds
  are retained for manual reconciliation instead of under-revoking
- Production startup validation that rejects fake verifiers, signature bypasses, development storage, missing secrets, unsafe sandbox configuration, and an empty product allowlist
- Bounded UTF-8 request bodies and normalized correlation IDs

These controls do not remove the need for store-sandbox testing, secret rotation, monitoring, alerting, and a deployment review.

## Functions and routes

| Function | Route | Azure authorization | Additional trust boundary |
|---|---|---|---|
| `VerifyPurchase` | `POST /api/monetization/verify` | Function key | PlayFab wrapper required outside local/test |
| `GetEntitlements` | `POST /api/monetization/entitlements` | Function key | PlayFab wrapper required outside local/test |
| `AppleNotifications` | `POST /api/webhooks/apple` | Anonymous route | Apple signed payload and configured app identity |
| `GoogleRtdn` | `POST /api/webhooks/google` | Anonymous route | Google OIDC bearer token, audience, service account, and message validation |

`Anonymous` webhook routes are intentional because stores cannot use Azure Function keys. Their handlers authenticate the signed store message instead.

The legacy `IapVerify`, `IapGetEntitlements`, and `GrantPurchaseRewards` functions in `Samples~/GameApiBackend` are disabled migration stubs and always return `410 Gone / LEGACY_MONETIZATION_DISABLED`.

## Local development

From `cloudscript-azure-functions-monetization`:

1. Copy `local.settings.template.json` to `local.settings.json`.
2. Keep `AZURE_FUNCTIONS_ENVIRONMENT=Development`.
3. Start Azurite when using `UseDevelopmentStorage=true`.
4. Configure at least one enabled product in `ALLOWED_PRODUCTS_JSON`.
5. Set `USE_FAKE_VERIFIER=true` only for an explicit fake-receipt test.
6. Start the host:

```bash
func start
```

The fake verifier accepts arbitrary receipts and marks them as sandbox purchases. It provides no security evidence. Use Apple/Google sandbox verification before release.

## Production configuration

The startup validator requires at least one store. Set `APPLE_STORE_ENABLED=false` for an
Android-only deployment or `GOOGLE_STORE_ENABLED=false` for an iOS-only deployment; disabled
stores do not require credentials and their verification/webhook routes fail closed.

### Core settings

| Setting | Production requirement |
|---|---|
| `AZURE_FUNCTIONS_ENVIRONMENT` | Set explicitly to `Production` |
| `MONETIZATION_STORAGE_CONNECTION` | Real Azure Storage connection; never `UseDevelopmentStorage=true` |
| `PLAYFAB_TITLE_ID` | Expected title ID |
| `PLAYFAB_DEV_SECRET_KEY` | Server-side secret from a secret manager |
| `USE_FAKE_VERIFIER` | `false` |
| `ALLOWED_PRODUCTS_JSON` | At least one enabled product |
| `ALLOW_SANDBOX_IN_PRODUCTION` | `false` for a real production deployment |
| `APPLE_STORE_ENABLED` | `true` when Apple verification/webhooks are deployed |
| `GOOGLE_STORE_ENABLED` | `true` when Google verification/RTDN are deployed |

### Apple settings

| Setting | Meaning |
|---|---|
| `APPLE_BUNDLE_ID` | Exact application bundle ID |
| `APPLE_APP_ID` | Positive numeric Apple app ID |
| `APPLE_ISSUER_ID` | App Store Connect issuer ID |
| `APPLE_KEY_ID` | App Store Connect key ID |
| `APPLE_PRIVATE_KEY_BASE64` | Base64-encoded private key material |
| `APPLE_ROOT_CA_BASE64` | Base64 DER trust anchors; separate multiple roots with `;` |
| `APPLE_ENVIRONMENT` | `Production` or `Sandbox` |
| `APPLE_CERTIFICATE_REVOCATION_MODE` | `Online` or `Offline`; `NoCheck` is rejected outside development |
| `APPLE_MAX_NOTIFICATION_AGE_SECONDS` | `60` through `604800` |
| `APPLE_SKIP_SIGNATURE_VALIDATION` | Must be `false` outside local/test |
| `APPLE_REQUIRE_APP_ACCOUNT_TOKEN` | Must be `true` outside explicit local migration testing |

Apple signature/certificate validation is implemented. Do not enable the skip flag to work around certificate or configuration errors.

For Apple purchases, the Unity client sets a deterministic UUIDv8 `appAccountToken` before store initialization. The backend independently derives the expected UUID from the trusted PlayFab title-player ID and Guid-compares it with Apple's signed transaction. Missing/mismatched bindings, revoked transactions, expired subscriptions, product-type mismatches, and quantities other than one fail closed. Existing purchases created before this binding was enabled require an explicit restore/migration plan; do not disable the production check as a silent compatibility fallback.

Apple verification sends only `transactionId` to App Store Server API. The Unity pending store and backend request deliberately leave `receiptPayload` empty for Apple, so AppReceipt/JWS data is not persisted or transported. Signed `REFUND`/`REVOKE` notifications reconcile one-time and subscription grants by canonical transaction identity. Full one-time refunds revoke recorded unit grants; prorated or ambiguous quantities return a retryable manual-reconciliation error. Validate these paths in App Store sandbox before launch.

### Google settings

| Setting | Meaning |
|---|---|
| `GOOGLE_PACKAGE_NAME` | Exact Android package name |
| `GOOGLE_SERVICE_ACCOUNT_EMAIL` | Service account used for Android Publisher API access |
| `GOOGLE_PRIVATE_KEY_BASE64` | Base64-encoded service-account private key |
| `GOOGLE_PUBSUB_AUDIENCE` | Exact audience configured on the Pub/Sub push subscription |
| `GOOGLE_PUBSUB_SERVICE_ACCOUNT_EMAIL` | Exact verified OIDC token email |
| `GOOGLE_MAX_MESSAGE_AGE_SECONDS` | `60` through `604800` |
| `GOOGLE_REQUIRE_OBFUSCATED_ACCOUNT_ID` | Must be `true` outside explicit local fake-receipt testing |

Store all secrets in Function App settings backed by a managed secret solution. Never commit `local.settings.json` or log credentials, raw receipts, private keys, bearer tokens, or function keys.

For Google purchases, the Unity client sets the obfuscated account ID before store initialization. The backend ignores caller-supplied identity, derives the expected uppercase SHA-256 value from the trusted PlayFab title-player ID, and compares it with the store response. A missing or mismatched binding fails closed. Existing games enabling this policy must plan how pre-binding purchases will be restored or migrated; do not silently disable the production check.

Google transaction idempotency uses `google:` plus SHA-256 of the raw purchase token. The client-provided transaction/order ID is never the canonical key. The raw token is neither persisted as a key nor written to application/dependency telemetry.

Google one-time verification also validates the response product/token when present, accepts
only quantity `1`, and rejects any purchase whose refundable quantity proves a partial or full
refund. Supporting multi-quantity store purchases requires an explicit server grant-scaling and
refund policy; the template intentionally fails closed until a game implements one.

`GetEntitlements` exchanges the PlayFab secret for a short-lived title entity token, then reads
all Economy v2 inventory pages. It returns each stack's actual 64-bit amount, stack ID, and
optional expiry. A PlayFab/network/malformed-response failure returns `503
INVENTORY_UNAVAILABLE`; it is never converted into an authoritative empty inventory.

PlayFab Economy v2 retains an `IdempotencyId` for 14 days. The service durably records the first outbound grant attempt and allows automatic retries for at most 13 days. At or beyond that conservative boundary it returns non-retryable `GRANT_RECONCILIATION_REQUIRED`; inspect provider inventory and reconcile manually instead of risking a duplicate grant. Legacy `Verified` rows without a first-attempt timestamp also fail closed.

Subscription activation grants derive their idempotency key from the provider subscription identity (Apple original transaction ID or the Google token hash), not an individual renewal transaction. A verified Apple renewal that matches the player's active durable product/tier/item snapshot updates the subscription projection without adding the Economy item again. A different active subscription key returns `SUBSCRIPTION_CHANGE_NOT_SUPPORTED`; implement an explicit upgrade/downgrade flow before enabling product changes. Missing authoritative purchase/expiry dates are rejected rather than replaced with guessed billing periods.

## Product allowlist

`ALLOWED_PRODUCTS_JSON` is the server authority for products and grants:

```json
{
  "products": {
    "com.company.game.coins_100": {
      "productId": "com.company.game.coins_100",
      "type": "Consumable",
      "economyItemIds": ["currency_coins"],
      "quantity": 100,
      "grantMetadata": {
        "source": "iap",
        "offer": "coins-100"
      },
      "enabled": true
    },
    "com.company.game.premium_monthly": {
      "productId": "com.company.game.premium_monthly",
      "type": "Subscription",
      "economyItemIds": ["subscription_premium"],
      "tierKey": "premium",
      "tierPrecedence": 1,
      "enabled": true
    }
  }
}
```

The dictionary key, `productId`, store-console ID, and Unity catalog ID must match exactly. Startup also rejects empty/duplicate/overlong item IDs, unsafe quantities, invalid tier fields, oversized catalogs, and oversized grant metadata. `grantMetadata` is optional server-authoritative inventory metadata: at most 16 entries, 64 characters per key, 512 per value, and 4 KiB total UTF-8 data. Treat changes to items, quantities, metadata, and tier precedence as reviewed production economy changes.

The immutable item/quantity/metadata/tier payload is persisted on the first purchase claim and reused by retries even if the live allowlist later changes. Client-supplied request `metadata` is retained only for wire compatibility and is never persisted or forwarded to PlayFab.

## Client request contract

The Unity backend SDK sends an inner envelope through PlayFab ExecuteFunction. A verification payload has this shape:

```json
{
  "functionName": "VerifyPurchase",
  "correlationId": "3ac4a3de5e6246cebe25d9ca4a62c477",
  "idempotencyKey": "550e8400-e29b-41d4-a716-446655440000",
  "payload": {
    "platform": "google",
    "productId": "com.company.game.coins_100",
    "receiptPayload": "<google-purchase-token>",
    "productType": "Consumable"
  },
  "caller": {}
}
```

Outside local/test, caller values in this inner envelope are ignored and replaced from PlayFab's trusted wrapper. Do not call the Function URL directly from a shipped client.

`transactionId` is required for Apple and `receiptPayload` must be empty. It is optional and non-canonical for Google, where `receiptPayload` is the purchase token. `productType`, `tierKey`, and `metadata` are compatibility fields only; the server allowlist owns grant semantics.

## Deployment

Deploy this project to a Function App separate from gameplay CloudScript:

```bash
func azure functionapp publish YOUR_MONETIZATION_FUNCTION_APP --dotnet-isolated
```

After deployment:

1. Register `VerifyPurchase` and `GetEntitlements` in PlayFab Automation against this Function App's function-key URLs.
2. Configure App Store Server Notifications v2 to `/api/webhooks/apple`.
3. Configure Google Pub/Sub authenticated push to `/api/webhooks/google`, using the exact audience and service-account email from app settings.
4. Exercise one sandbox transaction and one duplicate delivery per platform.
5. Verify grants, transaction records, logs, webhook retries, refund/revoke behavior, and entitlement removal.

## Tests

From the repository root:

```bash
dotnet test Samples~/GameApiBackend/tests/Serhat.Forge.CloudScript.Tests.csproj \
  --configuration Release \
  --property:TreatWarningsAsErrors=true
```

The test project references both the gameplay sample and this monetization project. It covers verification/idempotency behavior, lifecycle transitions, request hardening, and webhook parsing/authentication. Unit tests use fakes; they do not replace end-to-end Apple/Google sandbox tests.
