using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Forge.Startup;
using UnityEngine;

namespace Serhat.Forge.Analytics
{
    /// <summary>Optional startup-pipeline adapter for the generic analytics manager.</summary>
    public sealed class AnalyticsStartupStep : StartupStep
    {
        [SerializeField] private AnalyticsManager _manager;

        public override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_manager == null)
                throw new InvalidOperationException("AnalyticsManager is not assigned.");

            await _manager.InitializeAsync(cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_manager.IsInitialized)
                throw new InvalidOperationException("Analytics initialization did not complete successfully.");
        }
    }
}
