# Game API Reference sample

This optional sample demonstrates how to layer strongly typed game contracts on top of the transport-agnostic Serhat Backend SDK.

It contains example concepts such as levels, lives, currencies, boosters, daily rewards, and progression. Replace those contracts with your own game domain; do not copy them into the core SDK.

## Enable

1. Import **Game API Reference** from Package Manager Samples.
2. Install and configure the optional PlayFab SDK if you want to use the provided transport.
3. Add SERHAT_FORGE_GAME_API_SAMPLE to the target's scripting define symbols. The integration assembly also requires the PlayFab SDK's PLAYFAB_SDK symbol.
4. Start with GameApiSample or adapt BackendManager to your composition root.

The sample is excluded from compilation until the define is enabled. No title ID, developer secret, session ticket, or production endpoint is included.