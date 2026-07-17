using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Forge;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Serhat.Forge.Content
{
	/// <summary>
	/// Centralized manager for all Addressables content operations.
	/// Provides a clean API for loading, preloading, and releasing content.
	/// Other systems should use this manager instead of calling Addressables directly.
	/// </summary>
	public sealed class AddressablesContentManager : MonoBehaviour, IContentManager
	{
		private const float DownloadProgressUpdateIntervalSeconds = 0.1f;

		private bool _initialized;
		private Task _initTask;
		private readonly HashSet<IContentHandle> _activeHandles = new HashSet<IContentHandle>();
		private readonly object _handleLock = new object();

		/// <summary>
		/// Whether the Addressables system has been initialized.
		/// </summary>
		public bool IsInitialized => _initialized;

		/// <summary>
		/// Event raised when download progress changes.
		/// </summary>
		public event DownloadProgressHandler OnDownloadProgress;

		private void OnDestroy()
		{
			ReleaseAll();
		}

		/// <summary>
		/// Initializes the Addressables system.
		/// Must be called before any other content operations.
		/// </summary>
		/// <param name="ct">Cancellation token.</param>
		/// <returns>Result of the initialization.</returns>
		public async Task<ContentOperationResult> InitializeAsync(CancellationToken ct = default)
		{
			if (_initialized)
			{
				return ContentOperationResult.Success();
			}

			// Ensure only one initialization runs at a time
			if (_initTask == null)
			{
				_initTask = InitializeInternalAsync(ct);
			}

			var initTask = _initTask;

			try
			{
				await initTask;
				return _initialized ? ContentOperationResult.Success() : ContentOperationResult.Failure(ContentLoadStatus.Error, "Initialization failed.");
			}
			catch (OperationCanceledException)
			{
				return ContentOperationResult.Failure(ContentLoadStatus.Cancelled, "Initialization cancelled.");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ContentManager] Initialization exception: {ex.Message}");
				return ContentOperationResult.FromException(ex);
			}
			finally
			{
				if (initTask == _initTask && initTask.IsCompleted)
				{
					_initTask = null;
				}
			}
		}

		private async Task InitializeInternalAsync(CancellationToken ct)
		{
			Debug.Log("[ContentManager] Initializing Addressables...");

			// Keep the handle valid for status checks; default auto-release invalidates it on completion.
			var typedHandle = Addressables.InitializeAsync(false);

			if (!typedHandle.IsValid())
			{
				throw new InvalidOperationException("Addressables initialization returned an invalid handle. Ensure runtime data exists for the active build target.");
			}

			try
			{
				// Poll until done to support cancellation.
				while (!typedHandle.IsDone)
				{
					if (ct.IsCancellationRequested)
					{
						throw new OperationCanceledException(ct);
					}
					await Task.Yield();
				}

				if (typedHandle.Status == AsyncOperationStatus.Succeeded)
				{
					_initialized = true;
					Debug.Log("[ContentManager] Addressables initialized successfully.");
				}
				else
				{
					var error = typedHandle.OperationException?.Message ?? "Unknown initialization error.";
					Debug.LogError($"[ContentManager] Initialization failed: {error}");
					throw typedHandle.OperationException ?? new Exception(error);
				}
			}
			finally
			{
				if (typedHandle.IsValid())
				{
					Addressables.Release(typedHandle);
				}
			}
		}

		/// <summary>
		/// Checks for and downloads catalog updates.
		/// </summary>
		/// <param name="ct">Cancellation token.</param>
		/// <returns>Result of the operation.</returns>
		public async Task<ContentOperationResult> CheckAndUpdateCatalogsAsync(CancellationToken ct = default)
		{
			if (!EnsureInitialized(out var error))
			{
				return error;
			}

			try
			{
				RaiseProgress(DownloadProgress.CheckingCatalog());
				Debug.Log("[ContentManager] Checking for catalog updates...");

				var checkHandle = Addressables.CheckForCatalogUpdates(false);
				await WaitForOperationAsync(checkHandle, ct);

				if (ct.IsCancellationRequested)
				{
					Addressables.Release(checkHandle);
					return ContentOperationResult.Failure(ContentLoadStatus.Cancelled, "Catalog check cancelled.");
				}

				if (checkHandle.Status != AsyncOperationStatus.Succeeded)
				{
					var checkError = checkHandle.OperationException?.Message ?? "Failed to check for catalog updates.";
					Debug.LogWarning($"[ContentManager] Catalog check failed: {checkError}");
					Addressables.Release(checkHandle);
					return ContentOperationResult.Failure(ContentLoadStatus.NetworkError, checkError, checkHandle.OperationException);
				}

				var catalogsToUpdate = checkHandle.Result;
				Addressables.Release(checkHandle);

				if (catalogsToUpdate == null || catalogsToUpdate.Count == 0)
				{
					Debug.Log("[ContentManager] No catalog updates available.");
					RaiseProgress(DownloadProgress.Completed());
					return ContentOperationResult.Success();
				}

				Debug.Log($"[ContentManager] Updating {catalogsToUpdate.Count} catalog(s)...");
				RaiseProgress(DownloadProgress.UpdatingCatalog(0f));

				var updateHandle = Addressables.UpdateCatalogs(catalogsToUpdate, false);
				await WaitForOperationWithProgressAsync(updateHandle, ct, progress =>
				{
					RaiseProgress(DownloadProgress.UpdatingCatalog(progress));
				});

				if (ct.IsCancellationRequested)
				{
					Addressables.Release(updateHandle);
					return ContentOperationResult.Failure(ContentLoadStatus.Cancelled, "Catalog update cancelled.");
				}

				if (updateHandle.Status != AsyncOperationStatus.Succeeded)
				{
					var updateError = updateHandle.OperationException?.Message ?? "Failed to update catalogs.";
					Debug.LogError($"[ContentManager] Catalog update failed: {updateError}");
					Addressables.Release(updateHandle);
					RaiseProgress(DownloadProgress.Failed(updateError));
					return ContentOperationResult.Failure(ContentLoadStatus.NetworkError, updateError, updateHandle.OperationException);
				}

				Addressables.Release(updateHandle);
				Debug.Log("[ContentManager] Catalogs updated successfully.");
				RaiseProgress(DownloadProgress.Completed());
				return ContentOperationResult.Success();
			}
			catch (OperationCanceledException)
			{
				return ContentOperationResult.Failure(ContentLoadStatus.Cancelled, "Catalog update cancelled.");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ContentManager] Catalog update exception: {ex.Message}");
				RaiseProgress(DownloadProgress.Failed(ex.Message));
				return ContentOperationResult.FromException(ex);
			}
		}

		/// <summary>
		/// Gets the download size for a label or key.
		/// </summary>
		/// <param name="labelOrKey">The label or key to check.</param>
		/// <param name="ct">Cancellation token.</param>
		/// <returns>Download size in bytes, or 0 if already cached or not found.</returns>
		public async Task<long> GetDownloadSizeAsync(string labelOrKey, CancellationToken ct = default)
		{
			if (!EnsureInitialized(out _) || string.IsNullOrEmpty(labelOrKey))
			{
				return 0;
			}

			AsyncOperationHandle<long> handle = default;

			try
			{
				handle = Addressables.GetDownloadSizeAsync(labelOrKey);
				await WaitForOperationAsync(handle, ct);

				if (ct.IsCancellationRequested || handle.Status != AsyncOperationStatus.Succeeded)
				{
					return 0;
				}

				return handle.Result;
			}
			catch
			{
				return 0;
			}
			finally
			{
				if (handle.IsValid())
				{
					Addressables.Release(handle);
				}
			}
		}

		/// <summary>
		/// Ensures content for a label is downloaded and cached.
		/// Use this to preload content before it's needed.
		/// </summary>
		/// <param name="labelOrKey">The label or key to preload.</param>
		/// <param name="ct">Cancellation token.</param>
		/// <returns>True if content is ready, false otherwise.</returns>
		public async Task<bool> EnsureContentAsync(string labelOrKey, CancellationToken ct = default)
		{
			if (!EnsureInitialized(out _) || string.IsNullOrEmpty(labelOrKey))
			{
				return false;
			}

			AsyncOperationHandle<long> sizeHandle = default;

			try
			{
				RaiseProgress(DownloadProgress.CalculatingSize(labelOrKey));
				Debug.Log($"[ContentManager] Ensuring content for label: {labelOrKey}");

				sizeHandle = Addressables.GetDownloadSizeAsync(labelOrKey);
				await WaitForOperationAsync(sizeHandle, ct);

				if (ct.IsCancellationRequested)
				{
					return false;
				}

				if (sizeHandle.Status != AsyncOperationStatus.Succeeded)
				{
					Debug.LogWarning($"[ContentManager] Failed to get download size for {labelOrKey}");
					return false;
				}

				var downloadSize = sizeHandle.Result;

				if (downloadSize <= 0)
				{
					Debug.Log($"[ContentManager] Content for {labelOrKey} is already cached.");
					RaiseProgress(DownloadProgress.Completed());
					return true;
				}

				Debug.Log($"[ContentManager] Downloading {DownloadProgress.Downloading(labelOrKey, 0, downloadSize, 0).FormattedTotalSize} for {labelOrKey}...");

				var downloadHandle = Addressables.DownloadDependenciesAsync(labelOrKey, false);
				var totalBytes = downloadSize;

				await WaitForDownloadOperationWithProgressAsync(downloadHandle, totalBytes, ct, (progress, reportedTotal, downloadedBytes) =>
				{
					RaiseProgress(DownloadProgress.Downloading(labelOrKey, progress, reportedTotal, downloadedBytes));
				}, DownloadProgressUpdateIntervalSeconds);

				if (ct.IsCancellationRequested)
				{
					Addressables.Release(downloadHandle);
					RaiseProgress(DownloadProgress.Failed("Download cancelled."));
					return false;
				}

				if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
				{
					var downloadError = downloadHandle.OperationException?.Message ?? "Download failed.";
					Debug.LogError($"[ContentManager] Download failed for {labelOrKey}: {downloadError}");
					Addressables.Release(downloadHandle);
					RaiseProgress(DownloadProgress.Failed(downloadError));
					return false;
				}

				Addressables.Release(downloadHandle);
				Debug.Log($"[ContentManager] Content for {labelOrKey} downloaded successfully.");
				RaiseProgress(DownloadProgress.Completed());
				return true;
			}
			catch (OperationCanceledException)
			{
				RaiseProgress(DownloadProgress.Failed("Download cancelled."));
				return false;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ContentManager] EnsureContent exception for {labelOrKey}: {ex.Message}");
				RaiseProgress(DownloadProgress.Failed(ex.Message));
				return false;
			}
			finally
			{
				if (sizeHandle.IsValid())
				{
					Addressables.Release(sizeHandle);
				}
			}
		}

		/// <summary>
		/// Ensures content for multiple labels is downloaded with unified progress reporting.
		/// Progress is reported as a single continuous 0-1 value across all labels.
		/// </summary>
		/// <param name="labels">Labels to preload.</param>
		/// <param name="ct">Cancellation token.</param>
		/// <returns>True if all content is ready.</returns>
		public async Task<bool> EnsureContentAsync(IEnumerable<string> labels, CancellationToken ct = default)
		{
			if (labels == null)
			{
				return false;
			}

			var labelList = new List<string>();
			foreach (var label in labels)
			{
				if (!string.IsNullOrEmpty(label))
				{
					labelList.Add(label);
				}
			}

			if (labelList.Count == 0)
			{
				return true;
			}

			// Single label - use the original method
			if (labelList.Count == 1)
			{
				return await EnsureContentAsync(labelList[0], ct);
			}

			// Multiple labels - use batch download with unified progress
			return await EnsureContentBatchAsync(labelList, ct);
		}

		/// <summary>
		/// Downloads multiple labels with unified progress reporting.
		/// The slider progresses from 0 to 1 across all downloads combined.
		/// </summary>
		private async Task<bool> EnsureContentBatchAsync(List<string> labels, CancellationToken ct)
		{
			try
			{
				// Step 1: Calculate total download size for all labels
				RaiseProgress(DownloadProgress.CalculatingSize("all content"));
				Debug.Log($"[ContentManager] Calculating download size for {labels.Count} labels...");

				var labelSizes = new Dictionary<string, long>();
				long totalBytesToDownload = 0;

				foreach (var label in labels)
				{
					if (ct.IsCancellationRequested)
					{
						return false;
					}

					var size = await GetDownloadSizeAsync(label, ct);
					labelSizes[label] = size;
					totalBytesToDownload += size;
					Debug.Log($"[ContentManager] Label '{label}' download size: {DownloadProgress.Downloading(label, 0, size, 0).FormattedTotalSize}");
				}

				// If everything is cached, we're done
				if (totalBytesToDownload <= 0)
				{
					Debug.Log("[ContentManager] All content is already cached.");
					RaiseProgress(DownloadProgress.Completed());
					return true;
				}

				Debug.Log($"[ContentManager] Total download size: {DownloadProgress.Downloading("all", 0, totalBytesToDownload, 0).FormattedTotalSize}");

				// Step 2: Download each label while tracking cumulative progress
				long totalBytesDownloaded = 0;
				var allSuccess = true;

				for (int i = 0; i < labels.Count; i++)
				{
					if (ct.IsCancellationRequested)
					{
						RaiseProgress(DownloadProgress.Failed("Download cancelled."));
						return false;
					}

					var label = labels[i];
					var labelSize = labelSizes[label];

					// Skip if this label has no content to download
					if (labelSize <= 0)
					{
						Debug.Log($"[ContentManager] Label '{label}' is already cached, skipping.");
						continue;
					}

					Debug.Log($"[ContentManager] Downloading label '{label}' ({i + 1}/{labels.Count})...");

					var downloadHandle = Addressables.DownloadDependenciesAsync(label, false);
					var bytesDownloadedBefore = totalBytesDownloaded;

					await WaitForDownloadOperationWithProgressAsync(downloadHandle, labelSize, ct, (progress, reportedTotal, downloadedBytes) =>
					{
						// Calculate cumulative progress across all labels
						var cumulativeDownloaded = bytesDownloadedBefore + downloadedBytes;
						var overallProgress = totalBytesToDownload > 0 ? (float)cumulativeDownloaded / totalBytesToDownload : 0f;

						RaiseProgress(DownloadProgress.Downloading(label, overallProgress, totalBytesToDownload, cumulativeDownloaded));
					}, DownloadProgressUpdateIntervalSeconds);

					if (ct.IsCancellationRequested)
					{
						Addressables.Release(downloadHandle);
						RaiseProgress(DownloadProgress.Failed("Download cancelled."));
						return false;
					}

					if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
					{
						var downloadError = downloadHandle.OperationException?.Message ?? "Download failed.";
						Debug.LogError($"[ContentManager] Download failed for {label}: {downloadError}");
						Addressables.Release(downloadHandle);
						allSuccess = false;
						continue;
					}

					Addressables.Release(downloadHandle);
					totalBytesDownloaded += labelSize;
					Debug.Log($"[ContentManager] Label '{label}' downloaded successfully.");
				}

				if (allSuccess)
				{
					RaiseProgress(DownloadProgress.Completed());
				}

				return allSuccess;
			}
			catch (OperationCanceledException)
			{
				RaiseProgress(DownloadProgress.Failed("Download cancelled."));
				return false;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ContentManager] Batch download exception: {ex.Message}");
				RaiseProgress(DownloadProgress.Failed(ex.Message));
				return false;
			}
		}

		/// <summary>
		/// Loads an asset by key.
		/// </summary>
		/// <typeparam name="T">The type of asset to load.</typeparam>
		/// <param name="key">The Addressables key.</param>
		/// <param name="ct">Cancellation token.</param>
		/// <returns>The load result containing the asset and handle.</returns>
		public async Task<ContentLoadResult<T>> LoadAsync<T>(string key, CancellationToken ct = default)
		{
			if (!EnsureInitialized(out _))
			{
				return ContentLoadResult<T>.NotInitialized();
			}

			if (string.IsNullOrEmpty(key))
			{
				return ContentLoadResult<T>.NotFound("(empty key)");
			}

			try
			{
				Debug.Log($"[ContentManager] Loading asset: {key}");

				var handle = Addressables.LoadAssetAsync<T>(key);
				await WaitForOperationAsync(handle, ct);

				if (ct.IsCancellationRequested)
				{
					if (handle.IsValid())
					{
						Addressables.Release(handle);
					}

					return ContentLoadResult<T>.Cancelled();
				}

				if (handle.Status != AsyncOperationStatus.Succeeded)
				{
					var loadError = handle.OperationException?.Message ?? $"Failed to load {key}";
					Debug.LogWarning($"[ContentManager] Load failed for {key}: {loadError}");

					if (handle.IsValid())
					{
						Addressables.Release(handle);
					}

					return ContentLoadResult<T>.Failure(ContentLoadStatus.NotFound, loadError, handle.OperationException);
				}

				var contentHandle = new ContentHandle<T>(key, handle);
				TrackHandle(contentHandle);

				Debug.Log($"[ContentManager] Loaded asset: {key}");
				return ContentLoadResult<T>.Success(handle.Result, contentHandle);
			}
			catch (OperationCanceledException)
			{
				return ContentLoadResult<T>.Cancelled();
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ContentManager] Load exception for {key}: {ex.Message}");
				return ContentLoadResult<T>.FromException(ex);
			}
		}

		/// <summary>
		/// Loads all assets with a given label.
		/// </summary>
		/// <typeparam name="T">The type of assets to load.</typeparam>
		/// <param name="label">The label to load.</param>
		/// <param name="ct">Cancellation token.</param>
		/// <returns>List of loaded assets with their handles.</returns>
		public async Task<List<ContentLoadResult<T>>> LoadAllByLabelAsync<T>(string label, CancellationToken ct = default)
		{
			var results = new List<ContentLoadResult<T>>();

			if (!EnsureInitialized(out _) || string.IsNullOrEmpty(label))
			{
				return results;
			}

			try
			{
				Debug.Log($"[ContentManager] Loading all assets with label: {label}");

				var locationsHandle = Addressables.LoadResourceLocationsAsync(label, typeof(T));
				await WaitForOperationAsync(locationsHandle, ct);

				if (ct.IsCancellationRequested || locationsHandle.Status != AsyncOperationStatus.Succeeded)
				{
					Addressables.Release(locationsHandle);
					return results;
				}

				var locations = locationsHandle.Result;
				Addressables.Release(locationsHandle);

				foreach (var location in locations)
				{
					if (ct.IsCancellationRequested)
					{
						break;
					}

					var result = await LoadAsync<T>(location.PrimaryKey, ct);
					results.Add(result);
				}

				Debug.Log($"[ContentManager] Loaded {results.Count} assets with label: {label}");
				return results;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ContentManager] LoadAllByLabel exception for {label}: {ex.Message}");
				return results;
			}
		}

		/// <summary>
		/// Releases a content handle.
		/// </summary>
		/// <param name="handle">The handle to release.</param>
		public void Release(IContentHandle handle)
		{
			if (handle == null)
			{
				return;
			}

			UntrackHandle(handle);
			handle.Release();
		}

		/// <summary>
		/// Releases all tracked handles.
		/// </summary>
		public void ReleaseAll()
		{
			lock (_handleLock)
			{
				foreach (var handle in _activeHandles)
				{
					handle?.Release();
				}

				_activeHandles.Clear();
			}

			Debug.Log("[ContentManager] All handles released.");
		}

		/// <summary>
		/// Gets the count of active (unreleased) handles.
		/// </summary>
		public int ActiveHandleCount
		{
			get
			{
				lock (_handleLock)
				{
					return _activeHandles.Count;
				}
			}
		}

		/// <summary>
		/// Clears cached bundles for a label.
		/// </summary>
		/// <param name="labelOrKey">The label or key to clear.</param>
		/// <param name="ct">Cancellation token.</param>
		public async Task ClearCacheAsync(string labelOrKey, CancellationToken ct = default)
		{
			if (!EnsureInitialized(out _) || string.IsNullOrEmpty(labelOrKey))
			{
				return;
			}

			try
			{
				var handle = Addressables.ClearDependencyCacheAsync(labelOrKey, false);
				await WaitForOperationAsync(handle, ct);
				Addressables.Release(handle);
				Debug.Log($"[ContentManager] Cache cleared for: {labelOrKey}");
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[ContentManager] Failed to clear cache for {labelOrKey}: {ex.Message}");
			}
		}

		/// <summary>
		/// Checks if the network is available.
		/// Uses a simple connectivity check.
		/// </summary>
		public bool IsNetworkAvailable()
		{
			return Application.internetReachability != NetworkReachability.NotReachable;
		}

		private bool EnsureInitialized(out ContentOperationResult error)
		{
			if (!_initialized)
			{
				error = ContentOperationResult.Failure(ContentLoadStatus.NotInitialized, "ContentManager not initialized. Call InitializeAsync first.");
				return false;
			}

			error = ContentOperationResult.Success();
			return true;
		}

		private void TrackHandle(IContentHandle handle)
		{
			if (handle == null)
			{
				return;
			}

			lock (_handleLock)
			{
				_activeHandles.Add(handle);
			}
		}

		private void UntrackHandle(IContentHandle handle)
		{
			if (handle == null)
			{
				return;
			}

			lock (_handleLock)
			{
				_activeHandles.Remove(handle);
			}
		}

		private void RaiseProgress(DownloadProgress progress)
		{
			try
			{
				OnDownloadProgress?.Invoke(progress);
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[ContentManager] Progress handler exception: {ex.Message}");
			}
		}

		private static async Task WaitForOperationAsync<T>(AsyncOperationHandle<T> handle, CancellationToken ct)
		{
			while (!handle.IsDone && !ct.IsCancellationRequested)
			{
				await Task.Yield();
			}
		}

		private static async Task WaitForOperationAsync(AsyncOperationHandle handle, CancellationToken ct)
		{
			while (!handle.IsDone && !ct.IsCancellationRequested)
			{
				await Task.Yield();
			}
		}

		private static async Task WaitForOperationWithProgressAsync<T>(AsyncOperationHandle<T> handle, CancellationToken ct, Action<float> onProgress)
		{
			var lastProgress = -1f;

			while (!handle.IsDone && !ct.IsCancellationRequested)
			{
				var progress = handle.PercentComplete;
				if (Math.Abs(progress - lastProgress) > 0.01f)
				{
					lastProgress = progress;
					onProgress?.Invoke(progress);
				}

				await Task.Yield();
			}

			if (handle.IsDone && !ct.IsCancellationRequested)
			{
				onProgress?.Invoke(1f);
			}
		}

		private static async Task WaitForOperationWithProgressAsync(AsyncOperationHandle handle, CancellationToken ct, Action<float> onProgress)
		{
			var lastProgress = -1f;

			while (!handle.IsDone && !ct.IsCancellationRequested)
			{
				var progress = handle.PercentComplete;
				if (Math.Abs(progress - lastProgress) > 0.01f)
				{
					lastProgress = progress;
					onProgress?.Invoke(progress);
				}

				await Task.Yield();
			}

			if (handle.IsDone && !ct.IsCancellationRequested)
			{
				onProgress?.Invoke(1f);
			}
		}

		private static async Task WaitForDownloadOperationWithProgressAsync(
			AsyncOperationHandle handle,
			long fallbackTotalBytes,
			CancellationToken ct,
			Action<float, long, long> onProgress,
			float minUpdateIntervalSeconds)
		{
			var lastProgress = -1f;
			var lastDownloadedBytes = -1L;
			var lastTotalBytes = -1L;
			var minInterval = Mathf.Max(0f, minUpdateIntervalSeconds);
			var lastUpdateTime = -minInterval;

			while (!handle.IsDone && !ct.IsCancellationRequested)
			{
				if (minInterval > 0f && Time.unscaledTime - lastUpdateTime < minInterval)
				{
					await Task.Yield();
					continue;
				}

				lastUpdateTime = Time.unscaledTime;

				var status = handle.GetDownloadStatus();
				long totalBytes;
				long downloadedBytes;
				float progress;

				if (status.TotalBytes > 0)
				{
					totalBytes = status.TotalBytes;
					downloadedBytes = status.DownloadedBytes;
					progress = status.Percent;
				}
				else
				{
					totalBytes = fallbackTotalBytes;
					progress = handle.PercentComplete;
					downloadedBytes = totalBytes > 0 ? (long)(progress * totalBytes) : 0;
				}

				if (Math.Abs(progress - lastProgress) > 0.005f || downloadedBytes != lastDownloadedBytes || totalBytes != lastTotalBytes)
				{
					lastProgress = progress;
					lastDownloadedBytes = downloadedBytes;
					lastTotalBytes = totalBytes;
					onProgress?.Invoke(progress, totalBytes, downloadedBytes);
				}

				await Task.Yield();
			}

			if (handle.IsDone && !ct.IsCancellationRequested)
			{
				var status = handle.GetDownloadStatus();
				var totalBytes = status.TotalBytes > 0 ? status.TotalBytes : fallbackTotalBytes;
				var downloadedBytes = status.TotalBytes > 0 ? status.DownloadedBytes : totalBytes;
				onProgress?.Invoke(1f, totalBytes, downloadedBytes);
			}
		}
	}
}
