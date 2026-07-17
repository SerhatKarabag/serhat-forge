# Serhat Analytics SDK

Preview analytics foundation used by Serhat Forge. It provides provider abstraction, serialized dispatch, batching, and a persistent offline outbox.

The package is embedded in the template under `Packages/com.serhat.analytics-sdk`.

Optional providers are compile-gated. A remote-only configuration fails fast when no remote provider is available; debug-and-remote mode falls back to debug-only.

See the repository root `README.md` and `TEMPLATE_README.md` for setup and validation guidance.
