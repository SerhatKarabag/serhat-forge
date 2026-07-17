using System;

namespace Serhat.Forge.Content
{
	/// <summary>
	/// Result status for content loading operations.
	/// </summary>
	public enum ContentLoadStatus
	{
		/// <summary>
		/// Content loaded successfully.
		/// </summary>
		Success,

		/// <summary>
		/// Content not found in Addressables.
		/// </summary>
		NotFound,

		/// <summary>
		/// Network error while downloading.
		/// </summary>
		NetworkError,

		/// <summary>
		/// Operation was cancelled.
		/// </summary>
		Cancelled,

		/// <summary>
		/// Operation timed out.
		/// </summary>
		Timeout,

		/// <summary>
		/// Addressables system not initialized.
		/// </summary>
		NotInitialized,

		/// <summary>
		/// Unknown or unhandled error.
		/// </summary>
		Error
	}

	/// <summary>
	/// Result wrapper for content loading operations.
	/// </summary>
	/// <typeparam name="T">The type of the loaded content.</typeparam>
	public readonly struct ContentLoadResult<T>
	{
		/// <summary>
		/// The loaded asset, or default if loading failed.
		/// </summary>
		public readonly T Asset;

		/// <summary>
		/// The content handle for releasing the asset when done.
		/// May be null if loading failed.
		/// </summary>
		public readonly IContentHandle<T> Handle;

		/// <summary>
		/// The status of the load operation.
		/// </summary>
		public readonly ContentLoadStatus Status;

		/// <summary>
		/// Error message if loading failed.
		/// </summary>
		public readonly string ErrorMessage;

		/// <summary>
		/// The exception that caused the failure, if any.
		/// </summary>
		public readonly Exception Exception;

		/// <summary>
		/// Whether the load was successful.
		/// </summary>
		public bool IsSuccess => Status == ContentLoadStatus.Success;

		/// <summary>
		/// Whether the load failed.
		/// </summary>
		public bool IsFailure => !IsSuccess;

		private ContentLoadResult(T asset, IContentHandle<T> handle, ContentLoadStatus status, string errorMessage, Exception exception)
		{
			Asset = asset;
			Handle = handle;
			Status = status;
			ErrorMessage = errorMessage;
			Exception = exception;
		}

		/// <summary>
		/// Creates a successful result.
		/// </summary>
		public static ContentLoadResult<T> Success(T asset, IContentHandle<T> handle)
		{
			return new ContentLoadResult<T>(asset, handle, ContentLoadStatus.Success, null, null);
		}

		/// <summary>
		/// Creates a failure result.
		/// </summary>
		public static ContentLoadResult<T> Failure(ContentLoadStatus status, string errorMessage, Exception exception = null)
		{
			return new ContentLoadResult<T>(default, null, status, errorMessage, exception);
		}

		/// <summary>
		/// Creates a not found result.
		/// </summary>
		public static ContentLoadResult<T> NotFound(string key)
		{
			return Failure(ContentLoadStatus.NotFound, $"Content not found: {key}");
		}

		/// <summary>
		/// Creates a cancelled result.
		/// </summary>
		public static ContentLoadResult<T> Cancelled()
		{
			return Failure(ContentLoadStatus.Cancelled, "Operation was cancelled.");
		}

		/// <summary>
		/// Creates a not initialized result.
		/// </summary>
		public static ContentLoadResult<T> NotInitialized()
		{
			return Failure(ContentLoadStatus.NotInitialized, "Addressables system not initialized.");
		}

		/// <summary>
		/// Creates an error result from an exception.
		/// </summary>
		public static ContentLoadResult<T> FromException(Exception ex)
		{
			if (ex is OperationCanceledException)
			{
				return Cancelled();
			}

			var status = ContentLoadStatus.Error;
			var message = ex.Message;

			if (message != null && (message.Contains("network") || message.Contains("Network") || message.Contains("timeout")))
			{
				status = ContentLoadStatus.NetworkError;
			}

			return Failure(status, message, ex);
		}
	}

	/// <summary>
	/// Non-generic result for operations that don't return a typed asset.
	/// </summary>
	public readonly struct ContentOperationResult
	{
		public readonly ContentLoadStatus Status;
		public readonly string ErrorMessage;
		public readonly Exception Exception;

		public bool IsSuccess => Status == ContentLoadStatus.Success;
		public bool IsFailure => !IsSuccess;

		private ContentOperationResult(ContentLoadStatus status, string errorMessage, Exception exception)
		{
			Status = status;
			ErrorMessage = errorMessage;
			Exception = exception;
		}

		public static ContentOperationResult Success()
		{
			return new ContentOperationResult(ContentLoadStatus.Success, null, null);
		}

		public static ContentOperationResult Failure(ContentLoadStatus status, string errorMessage, Exception exception = null)
		{
			return new ContentOperationResult(status, errorMessage, exception);
		}

		public static ContentOperationResult FromException(Exception ex)
		{
			if (ex is OperationCanceledException)
			{
				return Failure(ContentLoadStatus.Cancelled, "Operation was cancelled.");
			}

			return Failure(ContentLoadStatus.Error, ex.Message, ex);
		}
	}
}
