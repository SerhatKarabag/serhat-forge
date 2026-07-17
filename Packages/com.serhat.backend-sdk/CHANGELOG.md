# Changelog

All notable changes to this package are documented here.

## [Unreleased]

- Fixed durable outbox state serialization by using the Unity-supported Newtonsoft.Json package for property and framework-value round trips.
- Added persistence coverage for queued commands, timestamps, and retry scheduling values.
- Made the Game API sample use the package-owned clock and corrected its initialization/invocation example.
- Expanded the Game API sample guide with authentication, composition, idempotency, outbox, error-handling, and production-boundary guidance.

## [2.1.0-preview.1] - 2026-07-17

- Moved the game-specific Game API surface into an optional Package Manager sample.
- Prepared package metadata for the Serhat Forge public preview.
