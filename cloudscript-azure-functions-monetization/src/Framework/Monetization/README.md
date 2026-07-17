# Serhat.Forge.CloudScript.Framework.Monetization

Server-side IAP verification and subscription management framework for Azure Functions.

## Features

- **Server-Authoritative Verification**: All purchase receipts are verified server-side with Apple/Google APIs
- **Idempotent Operations**: Transaction keys ensure purchases are processed exactly once
- **Subscription Lifecycle**: Full support for renewal, cancellation, grace period, refunds, chargebacks
- **PlayFab Economy v2 Integration**: Automatic entitlement grants via PlayFab inventory
- **Webhook Support**: Apple App Store Server Notifications v2 and Google RTDN
- **Multi-Tier Subscriptions**: Upgrade/downgrade support with tier policies

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Azure Functions                          │
├─────────────────────────────────────────────────────────────────┤
│  VerifyPurchaseFunction  │  GetEntitlementsFunction             │
│  AppleNotificationsFunc  │  GoogleRtdnFunction                  │
├─────────────────────────────────────────────────────────────────┤
│                         Services                                │
│  PurchaseVerificationService  │  SubscriptionLifecycleService   │
├─────────────────────────────────────────────────────────────────┤
│                       Abstractions                              │
│  IStoreVerifier  │  IPurchaseRepository  │  IEntitlementGranter │
├─────────────────────────────────────────────────────────────────┤
│                     Implementations                             │
│  AppleStoreVerifier    │  TableStoragePurchaseRepository        │
│  GooglePlayVerifier    │  PlayFabEconomyV2Granter               │
└─────────────────────────────────────────────────────────────────┘
```

## Configuration

### Environment Variables

```bash
# Required
PLAYFAB_TITLE_ID=<your-title-id>
PLAYFAB_SECRET_KEY=<your-secret-key>
MONETIZATION_STORAGE_CONNECTION=<azure-storage-connection-string>

# Apple App Store
APPLE_BUNDLE_ID=com.yourcompany.yourgame
APPLE_ISSUER_ID=<from-app-store-connect>
APPLE_KEY_ID=<from-app-store-connect>
APPLE_PRIVATE_KEY_BASE64=<base64-encoded-p8-key>
APPLE_ENVIRONMENT=Production  # or Sandbox

# Google Play
GOOGLE_PACKAGE_NAME=com.yourcompany.yourgame
GOOGLE_SERVICE_ACCOUNT_EMAIL=<service-account>@<project>.iam.gserviceaccount.com
GOOGLE_PRIVATE_KEY_BASE64=<base64-encoded-private-key>

# Product Configuration
ALLOWED_PRODUCTS_JSON=<json-product-config>

# Optional
USE_FAKE_VERIFIER=false  # Set true for testing
ALLOW_SANDBOX_IN_PRODUCTION=false
APPLE_SKIP_SIGNATURE_VALIDATION=false

> `false` is fail-closed: Apple production notifications are rejected until x5c/ES256 verification is implemented. Use `true` only for local tests.
```

### Product Configuration JSON

```json
{
  "products": {
    "coins_100": {
      "productId": "coins_100",
      "type": "Consumable",
      "economyItemIds": ["currency_coins"],
      "quantity": 100,
      "enabled": true
    },
    "premium_monthly": {
      "productId": "premium_monthly",
      "type": "Subscription",
      "economyItemIds": ["subscription_premium"],
      "tierKey": "premium",
      "tierPrecedence": 1,
      "enabled": true
    },
    "pro_monthly": {
      "productId": "pro_monthly",
      "type": "Subscription",
      "economyItemIds": ["subscription_pro"],
      "tierKey": "pro",
      "tierPrecedence": 2,
      "enabled": true
    }
  },
  "allowSandboxInProduction": false
}
```

## API Endpoints

### POST /api/monetization/verify

Verify and process a purchase.

**Request:**
```json
{
  "correlationId": "uuid",
  "idempotencyKey": "unique-key",
  "caller": { "playerId": "player123" },
  "payload": {
    "platform": "apple",
    "productId": "coins_100",
    "transactionId": "1000000123456789",
    "receiptPayload": "<base64-receipt>",
    "packageName": "com.yourcompany.yourgame"
  }
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "success": true,
    "transactionKey": "apple:1000000123456789",
    "grantedItemIds": ["currency_coins"],
    "subscription": null
  },
  "correlationId": "uuid",
  "processingTimeMs": 150
}
```

### POST /api/monetization/entitlements

Get current entitlements for a player.

**Request:**
```json
{
  "correlationId": "uuid",
  "caller": { "playerId": "player123" },
  "payload": { "forceRefresh": false }
}
```

### POST /api/webhooks/apple

Apple App Store Server Notifications v2 endpoint.
Configure in App Store Connect under App > App Information > Server Notifications.

### POST /api/webhooks/google

Google Play RTDN endpoint.
Configure Pub/Sub push subscription to this URL.

## Subscription Lifecycle

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│   INITIAL    │────►│    ACTIVE    │────►│  CANCELLED   │
│   PURCHASE   │     │              │     │ (end of period)
└──────────────┘     └──────┬───────┘     └──────────────┘
                           │
                     ┌─────┴─────┐
                     │           │
                     ▼           ▼
              ┌──────────┐ ┌──────────┐
              │  GRACE   │ │  PAUSED  │
              │  PERIOD  │ │          │
              └────┬─────┘ └────┬─────┘
                   │            │
                   ▼            ▼
              ┌──────────┐ ┌──────────┐
              │ EXPIRED  │ │ RESUMED  │
              │          │ │          │
              └──────────┘ └──────────┘

Special States:
- REFUNDED: User requested refund
- CHARGEBACK: Payment dispute
- REVOKED: Developer/Store revoked
```

## Testing

Use `USE_FAKE_VERIFIER=true` for local development. The FakeStoreVerifier accepts any receipt and returns successful verification.

```bash
cd tests/Unit
dotnet test
```

## Security Notes

1. **Never expose store secrets to clients** - All verification happens server-side
2. **Transaction keys provide idempotency** - Same transaction can't be verified twice
3. **Webhook validation boundary** - Apple JWS is fail-closed until x5c/ES256 verification is implemented; never enable the skip flag in production
4. **Product allowlist** - Only configured products can be purchased
5. **Sandbox detection** - Sandbox purchases can be blocked in production
