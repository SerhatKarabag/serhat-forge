using System;
using System.Collections.Generic;
using UnityEngine;

namespace Serhat.Forge.Content
{
	/// <summary>
	/// Configuration for content loading and preloading behavior.
	/// </summary>
	[CreateAssetMenu(menuName = "Serhat Forge/Content Configuration", fileName = "ContentConfiguration")]
	public class ContentConfiguration : ScriptableObject
	{
		[Header("Initialization")]
		[Tooltip("Whether to check for catalog updates on boot.")]
		[SerializeField] private bool checkForCatalogUpdates;

		[Tooltip("Whether to continue with cached content if catalog update fails (offline mode).")]
		[SerializeField] private bool allowOfflineMode = true;

		[Header("Preload Settings")]
		[Tooltip("Labels to preload during boot. These will be downloaded if not cached.")]
		[SerializeField] private List<string> bootPreloadLabels = new List<string>();

		[Tooltip("Whether boot can proceed if preload fails (will use cached content if available).")]
		[SerializeField] private bool allowBootWithoutPreload = true;

		[Header("Network Settings")]
		[Tooltip("Timeout in seconds for network operations.")]
		[SerializeField] private float networkTimeoutSeconds = 30f;

		[Tooltip("Number of retry attempts for failed network operations.")]
		[SerializeField] private int retryAttempts = 3;

		[Tooltip("Delay in seconds between retry attempts.")]
		[SerializeField] private float retryDelaySeconds = 1f;

		[Header("Debug")]
		[Tooltip("Enable verbose logging for content operations.")]
		[SerializeField] private bool verboseLogging;

		/// <summary>
		/// Whether to check for catalog updates on boot.
		/// </summary>
		public bool CheckForCatalogUpdates => checkForCatalogUpdates;

		/// <summary>
		/// Whether to continue with cached content if network is unavailable.
		/// </summary>
		public bool AllowOfflineMode => allowOfflineMode;

		/// <summary>
		/// Labels to preload during boot.
		/// </summary>
		public IReadOnlyList<string> BootPreloadLabels => bootPreloadLabels;

		/// <summary>
		/// Whether boot can proceed if preload fails.
		/// </summary>
		public bool AllowBootWithoutPreload => allowBootWithoutPreload;

		/// <summary>
		/// Network operation timeout in seconds.
		/// </summary>
		public float NetworkTimeoutSeconds => networkTimeoutSeconds;

		/// <summary>
		/// Number of retry attempts for failed operations.
		/// </summary>
		public int RetryAttempts => retryAttempts;

		/// <summary>
		/// Delay between retry attempts in seconds.
		/// </summary>
		public float RetryDelaySeconds => retryDelaySeconds;

		/// <summary>
		/// Whether verbose logging is enabled.
		/// </summary>
		public bool VerboseLogging => verboseLogging;

		/// <summary>
		/// Network timeout as TimeSpan.
		/// </summary>
		public TimeSpan NetworkTimeout => TimeSpan.FromSeconds(networkTimeoutSeconds);

		/// <summary>
		/// Retry delay as TimeSpan.
		/// </summary>
		public TimeSpan RetryDelay => TimeSpan.FromSeconds(retryDelaySeconds);

		/// <summary>
		/// Creates a default configuration for use when no asset is assigned.
		/// </summary>
		public static ContentConfiguration CreateDefault()
		{
			var config = CreateInstance<ContentConfiguration>();
			// A template must never contact a remote catalog unless the downstream
			// project explicitly opts in and configures an environment.
			config.checkForCatalogUpdates = false;
			config.allowOfflineMode = true;
			config.bootPreloadLabels = new List<string>();
			config.allowBootWithoutPreload = true;
			config.networkTimeoutSeconds = 30f;
			config.retryAttempts = 3;
			config.retryDelaySeconds = 1f;
			config.verboseLogging = false;
			return config;
		}
	}
}
