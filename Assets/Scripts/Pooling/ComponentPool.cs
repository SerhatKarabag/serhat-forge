using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace Serhat.Forge.Pooling
{
    /// <summary>Prefab-backed component pool with allocation-free steady-state get/release.</summary>
    public sealed class ComponentPool<T> : IDisposable where T : Component
    {
        private static readonly Predicate<T> DestroyedComponentPredicate = IsDestroyedComponent;

        private readonly T _prefab;
        private readonly Transform _poolRoot;
        private readonly ObjectPool<T> _pool;
        private readonly HashSet<T> _active;
        private readonly int _maxSize;
        private int _createdCount;
        private bool _disposed;

        public ComponentPool(
            T prefab,
            Transform poolRoot = null,
            int defaultCapacity = 8,
            int maxSize = 128,
            bool collectionCheck = true)
        {
            _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
            _poolRoot = poolRoot;
            _maxSize = Mathf.Max(1, maxSize);
            var initialCapacity = Mathf.Clamp(defaultCapacity, 1, _maxSize);
            _active = new HashSet<T>(_maxSize);
            _pool = new ObjectPool<T>(
                Create,
                null,
                OnRelease,
                DestroyInstance,
                collectionCheck,
                initialCapacity,
                _maxSize);
        }

        public int CountInactive => _pool.CountInactive;

        public int CountActive
        {
            get
            {
                PruneDestroyedLeasesInternal();
                return _active.Count;
            }
        }

        public int CountAll
        {
            get
            {
                PruneDestroyedLeasesInternal();
                return _createdCount;
            }
        }

        public T Get(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            ThrowIfDisposed();
            PruneDestroyedLeasesInternal();

            var instance = GetValidInstance();
            if (!_active.Add(instance))
                throw new InvalidOperationException("Pool returned an instance that is already active.");

            var instanceTransform = instance.transform;
            instanceTransform.SetParent(parent, false);
            instanceTransform.SetPositionAndRotation(position, rotation);
            instance.gameObject.SetActive(true);
            return instance;
        }

        public void Release(T instance)
        {
            ThrowIfDisposed();
            if (ReferenceEquals(instance, null))
                return;

            if (!_active.Remove(instance))
            {
                // A previously pruned Unity object is already untracked and safe to ignore.
                if (instance == null)
                    return;

                throw new InvalidOperationException(
                    "Instance is not leased from this pool or was already released.");
            }

            if (instance == null)
            {
                DecrementCreatedCount();
                return;
            }

            _pool.Release(instance);
        }

        /// <summary>
        /// Removes leased components that were destroyed outside the pool.
        /// Returns the number of stale leases removed.
        /// </summary>
        public int PruneDestroyedLeases()
        {
            ThrowIfDisposed();
            return PruneDestroyedLeasesInternal();
        }

        /// <summary>Ensures up to <paramref name="count"/> inactive instances exist.</summary>
        public void Prewarm(int count)
        {
            ThrowIfDisposed();
            PruneDestroyedLeasesInternal();

            var targetInactive = Mathf.Clamp(count, 0, _maxSize);
            var needed = Mathf.Min(
                targetInactive - _pool.CountInactive,
                _maxSize - _createdCount);
            if (needed <= 0)
                return;

            // Boot-only allocation; steady-state Get/Release remain allocation free.
            var rented = new T[needed];
            for (var i = 0; i < needed; i++)
                rented[i] = GetValidInstance();
            for (var i = 0; i < needed; i++)
                _pool.Release(rented[i]);
        }

        /// <summary>Destroys inactive instances. Active leases remain valid.</summary>
        public void Clear()
        {
            ThrowIfDisposed();
            _pool.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var instance in _active)
                DestroyInstance(instance);
            _active.Clear();
            _pool.Clear();
        }

        private T Create()
        {
            var instance = Object.Instantiate(_prefab, _poolRoot);
            instance.gameObject.SetActive(false);
            _createdCount++;
            return instance;
        }

        private T GetValidInstance()
        {
            while (true)
            {
                var instance = _pool.Get();
                if (instance != null)
                    return instance;

                // A released component can be destroyed through an external retained reference.
                // Unity's ObjectPool cannot adjust CountAll for that case, so track it locally.
                DecrementCreatedCount();
            }
        }

        private void OnRelease(T instance)
        {
            if (instance == null)
                return;

            instance.gameObject.SetActive(false);
            instance.transform.SetParent(_poolRoot, false);
        }

        private void DestroyInstance(T instance)
        {
            DecrementCreatedCount();
            if (instance != null)
                Object.Destroy(instance.gameObject);
        }

        private int PruneDestroyedLeasesInternal()
        {
            var removed = _active.RemoveWhere(DestroyedComponentPredicate);
            if (removed > 0)
                _createdCount = Mathf.Max(0, _createdCount - removed);

            return removed;
        }

        private void DecrementCreatedCount()
        {
            if (_createdCount > 0)
                _createdCount--;
        }

        private static bool IsDestroyedComponent(T instance) => instance == null;

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ComponentPool<T>));
        }
    }
}
