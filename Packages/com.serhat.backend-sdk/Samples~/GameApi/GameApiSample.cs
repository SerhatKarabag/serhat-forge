#nullable enable
using System;
using System.Threading;
using Serhat.Backend.Core;
using Serhat.Backend.GameApi;
using Serhat.Backend.PlayFab;
using UnityEngine;

namespace Serhat.Backend.Samples
{
    /// <summary>
    /// Sample MonoBehaviour demonstrating Game API Client usage.
    /// </summary>
    public class GameApiSample : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private string _titleId = "YOUR_PLAYFAB_TITLE_ID";
        [SerializeField] private string _environment = "development";

        private IGameApiClient? _client;
        private CancellationTokenSource? _cts;

        private async void Start()
        {
            _cts = new CancellationTokenSource();

            try
            {
                // Create the PlayFab invoker
                var options = new BackendSdkOptions
                {
                    TitleId = _titleId,
                    Environment = _environment
                };
                options.Retry.MaxAttempts = 3;
                options.Outbox.Enabled = true;
                options.EnableDetailedLogging = true;

                var invoker = new PlayFabCloudFunctionInvoker(
                    options,
                    new UnityJsonSerializer(),
                    new UnityBackendLogger(),
                    SystemClock.Instance);

                // Initialize the game API client
                _client = await GameApiClientBuilder.Create()
                    .WithTitleId(_titleId)
                    .WithEnvironment(_environment)
                    .WithInvoker(invoker)
                    .WithOptions(opts =>
                    {
                        opts.Retry.MaxAttempts = 3;
                        opts.Outbox.Enabled = true;
                        opts.EnableDetailedLogging = true;
                    })
                    .BuildAsync();

                Debug.Log("[Sample] Game API Client initialized successfully");

                // Example: Get bootstrap data
                await LoadBootstrap();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Sample] Failed to initialize Game API Client: {ex.Message}");
            }
        }

        private async void LoadBootstrap()
        {
            if (_client == null) return;

            Debug.Log("[Sample] Loading bootstrap...");

            var result = await _client.GetBootstrapAsync(_cts!.Token);

            result.Match(
                onSuccess: bootstrap =>
                {
                    Debug.Log($"[Sample] Bootstrap loaded:");
                    Debug.Log($"  - PlayerId: {bootstrap.Progress.PlayerId}");
                    Debug.Log($"  - CurrentLevel: {bootstrap.Progress.CurrentLevel}");
                },
                onFailure: error =>
                {
                    Debug.LogError($"[Sample] Failed to load bootstrap: {error}");
                });
        }

        /// <summary>
        /// Example: Submit a completed level result.
        /// </summary>
        public async void SubmitLevelResult(int levelId, float timeSec, int stars)
        {
            if (_client == null)
            {
                Debug.LogWarning("[Sample] Client not initialized");
                return;
            }

            Debug.Log($"[Sample] Submitting level {levelId} result...");

            var request = new SubmitLevelResultRequestDto
            {
                LevelId = levelId,
                TimeSec = timeSec,
                Stars = stars
            };

            var result = await _client.SubmitLevelResultAsync(request, ct: _cts!.Token);

            result.Match(
                onSuccess: levelResult =>
                {
                    Debug.Log($"[Sample] Level submitted. New current level: {levelResult.NewCurrentLevel}");
                },
                onFailure: error =>
                {
                    Debug.LogError($"[Sample] Failed to submit level: {error}");
                });
        }

        /// <summary>
        /// Check outbox status.
        /// </summary>
        public void CheckOutboxStatus()
        {
            if (_client == null) return;

            var status = _client.GetOutboxStatus();

            Debug.Log($"[Sample] Outbox status:");
            Debug.Log($"  - Pending: {status.PendingCount}");
            Debug.Log($"  - Dead letters: {status.DeadLetterCount}");
            Debug.Log($"  - Processing: {status.IsProcessing}");
            Debug.Log($"  - Last flush: {status.LastFlushAttemptUtc}");
        }

        /// <summary>
        /// Force flush the outbox.
        /// </summary>
        public async void FlushOutbox()
        {
            if (_client == null) return;

            Debug.Log("[Sample] Flushing outbox...");
            await _client.FlushOutboxAsync(_cts!.Token);
            Debug.Log("[Sample] Outbox flush complete");
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _client?.Dispose();
        }
    }
}
