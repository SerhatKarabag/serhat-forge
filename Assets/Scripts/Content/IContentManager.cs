using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Serhat.Forge.Content
{
    /// <summary>
    /// Interface for content management operations.
    /// Enables DI and testability for Addressables-based content loading.
    /// </summary>
    public interface IContentManager
    {
        /// <summary>
        /// Whether the content system has been initialized.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Event raised when download progress changes.
        /// </summary>
        event DownloadProgressHandler OnDownloadProgress;

        /// <summary>
        /// Initializes the content system.
        /// </summary>
        Task<ContentOperationResult> InitializeAsync(CancellationToken ct = default);

        /// <summary>
        /// Checks for and downloads catalog updates.
        /// </summary>
        Task<ContentOperationResult> CheckAndUpdateCatalogsAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets the download size for a label or key.
        /// </summary>
        Task<long> GetDownloadSizeAsync(string labelOrKey, CancellationToken ct = default);

        /// <summary>
        /// Ensures content for a label is downloaded and cached.
        /// </summary>
        Task<bool> EnsureContentAsync(string labelOrKey, CancellationToken ct = default);

        /// <summary>
        /// Ensures content for multiple labels is downloaded.
        /// </summary>
        Task<bool> EnsureContentAsync(IEnumerable<string> labels, CancellationToken ct = default);

        /// <summary>
        /// Loads an asset by key.
        /// </summary>
        Task<ContentLoadResult<T>> LoadAsync<T>(string key, CancellationToken ct = default);

        /// <summary>
        /// Loads all assets with a given label.
        /// </summary>
        Task<List<ContentLoadResult<T>>> LoadAllByLabelAsync<T>(string label, CancellationToken ct = default);

        /// <summary>
        /// Releases a content handle.
        /// </summary>
        void Release(IContentHandle handle);

        /// <summary>
        /// Releases all tracked handles.
        /// </summary>
        void ReleaseAll();

        /// <summary>
        /// Gets the count of active handles.
        /// </summary>
        int ActiveHandleCount { get; }

        /// <summary>
        /// Clears cached bundles for a label.
        /// </summary>
        Task ClearCacheAsync(string labelOrKey, CancellationToken ct = default);

        /// <summary>
        /// Checks if the network is available.
        /// </summary>
        bool IsNetworkAvailable();
    }
}
