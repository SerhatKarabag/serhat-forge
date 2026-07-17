using System;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Serhat.Forge.Content
{
	/// <summary>
	/// Interface for tracking and releasing Addressables handles.
	/// </summary>
	public interface IContentHandle : IDisposable
	{
		/// <summary>
		/// The Addressables key used to load this content.
		/// </summary>
		string Key { get; }

		/// <summary>
		/// Whether the handle is still valid and not released.
		/// </summary>
		bool IsValid { get; }

		/// <summary>
		/// Whether the asset is loaded.
		/// </summary>
		bool IsLoaded { get; }

		/// <summary>
		/// Release the handle and free the asset.
		/// </summary>
		void Release();
	}

	/// <summary>
	/// Generic content handle wrapper for type-safe access and automatic release.
	/// </summary>
	/// <typeparam name="T">The type of the loaded asset.</typeparam>
	public interface IContentHandle<out T> : IContentHandle
	{
		/// <summary>
		/// The loaded asset. Null if not loaded or released.
		/// </summary>
		T Asset { get; }
	}

	/// <summary>
	/// Concrete implementation of IContentHandle that wraps an AsyncOperationHandle.
	/// </summary>
	/// <typeparam name="T">The type of the loaded asset.</typeparam>
	public sealed class ContentHandle<T> : IContentHandle<T>
	{
		private AsyncOperationHandle<T> _handle;
		private readonly string _key;
		private bool _released;

		public string Key => _key;

		public bool IsValid => !_released && _handle.IsValid();

		public bool IsLoaded => IsValid && _handle.IsDone && _handle.Status == AsyncOperationStatus.Succeeded;

		public T Asset
		{
			get
			{
				if (!IsLoaded)
				{
					return default;
				}

				return _handle.Result;
			}
		}

		public ContentHandle(string key, AsyncOperationHandle<T> handle)
		{
			_key = key ?? string.Empty;
			_handle = handle;
			_released = false;
		}

		public void Release()
		{
			if (_released)
			{
				return;
			}

			_released = true;

			if (_handle.IsValid())
			{
				Addressables.Release(_handle);
			}

			_handle = default;
		}

		public void Dispose()
		{
			Release();
		}
	}

	/// <summary>
	/// Non-generic content handle for scenarios where the type is not known at compile time.
	/// </summary>
	public sealed class ContentHandleUntyped : IContentHandle
	{
		private AsyncOperationHandle _handle;
		private readonly string _key;
		private bool _released;

		public string Key => _key;

		public bool IsValid => !_released && _handle.IsValid();

		public bool IsLoaded => IsValid && _handle.IsDone && _handle.Status == AsyncOperationStatus.Succeeded;

		public object Asset
		{
			get
			{
				if (!IsLoaded)
				{
					return null;
				}

				return _handle.Result;
			}
		}

		public ContentHandleUntyped(string key, AsyncOperationHandle handle)
		{
			_key = key ?? string.Empty;
			_handle = handle;
			_released = false;
		}

		public void Release()
		{
			if (_released)
			{
				return;
			}

			_released = true;

			if (_handle.IsValid())
			{
				Addressables.Release(_handle);
			}

			_handle = default;
		}

		public void Dispose()
		{
			Release();
		}
	}
}
