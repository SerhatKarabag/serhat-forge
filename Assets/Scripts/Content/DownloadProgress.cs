using System;

namespace Serhat.Forge.Content
{
	/// <summary>
	/// Download phase during content acquisition.
	/// </summary>
	public enum DownloadPhase
	{
		/// <summary>
		/// Idle, no download in progress.
		/// </summary>
		Idle,

		/// <summary>
		/// Checking for catalog updates.
		/// </summary>
		CheckingCatalog,

		/// <summary>
		/// Downloading catalog updates.
		/// </summary>
		UpdatingCatalog,

		/// <summary>
		/// Calculating download size.
		/// </summary>
		CalculatingSize,

		/// <summary>
		/// Downloading content bundles.
		/// </summary>
		Downloading,

		/// <summary>
		/// Download completed successfully.
		/// </summary>
		Completed,

		/// <summary>
		/// Download failed with error.
		/// </summary>
		Failed
	}

	/// <summary>
	/// Progress data for content download operations.
	/// </summary>
	public readonly struct DownloadProgress
	{
		/// <summary>
		/// Current phase of the download.
		/// </summary>
		public readonly DownloadPhase Phase;

		/// <summary>
		/// Progress from 0 to 1.
		/// </summary>
		public readonly float Progress;

		/// <summary>
		/// Total bytes to download.
		/// </summary>
		public readonly long TotalBytes;

		/// <summary>
		/// Bytes downloaded so far.
		/// </summary>
		public readonly long DownloadedBytes;

		/// <summary>
		/// Label or key being downloaded.
		/// </summary>
		public readonly string CurrentLabel;

		/// <summary>
		/// Error message if phase is Failed.
		/// </summary>
		public readonly string ErrorMessage;

		/// <summary>
		/// Whether the download is complete (success or failure).
		/// </summary>
		public bool IsDone => Phase == DownloadPhase.Completed || Phase == DownloadPhase.Failed;

		/// <summary>
		/// Whether the download was successful.
		/// </summary>
		public bool IsSuccess => Phase == DownloadPhase.Completed;

		/// <summary>
		/// Formatted download size string.
		/// </summary>
		public string FormattedTotalSize => FormatBytes(TotalBytes);

		/// <summary>
		/// Formatted downloaded size string.
		/// </summary>
		public string FormattedDownloadedSize => FormatBytes(DownloadedBytes);

		/// <summary>
		/// Progress percentage (0-100).
		/// </summary>
		public int ProgressPercent => (int)(Progress * 100);

		public DownloadProgress(DownloadPhase phase, float progress, long totalBytes, long downloadedBytes, string currentLabel, string errorMessage)
		{
			Phase = phase;
			Progress = Math.Max(0f, Math.Min(1f, progress));
			TotalBytes = totalBytes;
			DownloadedBytes = downloadedBytes;
			CurrentLabel = currentLabel ?? string.Empty;
			ErrorMessage = errorMessage;
		}

		public static DownloadProgress Idle()
		{
			return new DownloadProgress(DownloadPhase.Idle, 0f, 0, 0, null, null);
		}

		public static DownloadProgress CheckingCatalog()
		{
			return new DownloadProgress(DownloadPhase.CheckingCatalog, 0f, 0, 0, null, null);
		}

		public static DownloadProgress UpdatingCatalog(float progress)
		{
			return new DownloadProgress(DownloadPhase.UpdatingCatalog, progress, 0, 0, null, null);
		}

		public static DownloadProgress CalculatingSize(string label)
		{
			return new DownloadProgress(DownloadPhase.CalculatingSize, 0f, 0, 0, label, null);
		}

		public static DownloadProgress Downloading(string label, float progress, long totalBytes, long downloadedBytes)
		{
			return new DownloadProgress(DownloadPhase.Downloading, progress, totalBytes, downloadedBytes, label, null);
		}

		public static DownloadProgress Completed()
		{
			return new DownloadProgress(DownloadPhase.Completed, 1f, 0, 0, null, null);
		}

		public static DownloadProgress Failed(string errorMessage)
		{
			return new DownloadProgress(DownloadPhase.Failed, 0f, 0, 0, null, errorMessage);
		}

		private static string FormatBytes(long bytes)
		{
			if (bytes <= 0)
			{
				return "0 B";
			}

			string[] units = { "B", "KB", "MB", "GB" };
			var unitIndex = 0;
			var size = (double)bytes;

			while (size >= 1024 && unitIndex < units.Length - 1)
			{
				size /= 1024;
				unitIndex++;
			}

			return $"{size:F1} {units[unitIndex]}";
		}
	}

	/// <summary>
	/// Interface for receiving download progress updates.
	/// </summary>
	public interface IDownloadProgressListener
	{
		/// <summary>
		/// Called when download progress changes.
		/// </summary>
		void OnDownloadProgress(DownloadProgress progress);
	}

	/// <summary>
	/// Delegate for download progress updates.
	/// </summary>
	public delegate void DownloadProgressHandler(DownloadProgress progress);
}
