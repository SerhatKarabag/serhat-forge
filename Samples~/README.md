# Optional reference projects

Everything under Samples~ is excluded from the default Unity compilation path. These projects demonstrate replaceable integrations; they are not required by Serhat Forge core.

## Game API backend

GameApiBackend is an Azure Functions and PlayFab reference for a level-based game economy. It intentionally contains domain concepts such as levels, lives, currencies, boosters, and rewards.

Use it when those concepts fit your game, or copy its transport, idempotency, validation, and observability patterns into your own backend contracts. Do not treat its economy rules as part of the template architecture.

The matching Unity client is available from the embedded Backend SDK package's **Game API Reference** sample. After importing that sample, enable SERHAT_FORGE_GAME_API_SAMPLE.

## Security

- Never commit local.settings.json, signing keys, service-account files, or store credentials.
- Use separate development, staging, and production environments.
- Keep fake verifiers and signature-validation bypasses disabled outside local development.
- Run the repository and cloud test suites before deploying a sample.