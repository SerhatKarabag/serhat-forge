using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Forge.Content
{
    /// <summary>
    /// Loads and owns Addressables prefab handles for application-lifetime synchronous access.
    /// </summary>
    public sealed class PrefabLoaderService : IPrefabLoader, IDisposable
    {
        private readonly IContentManager _contentManager;
        private readonly Dictionary<string, GameObject> _prefabs =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly List<IContentHandle> _handles = new List<IContentHandle>();

        public PrefabLoaderService(IContentManager contentManager)
        {
            _contentManager = contentManager ?? throw new ArgumentNullException(nameof(contentManager));
        }

        public bool IsLoaded { get; private set; }

        public async Task<bool> PreloadAsync(
            IEnumerable<string> keys,
            CancellationToken cancellationToken = default)
        {
            ReleaseAll();
            if (keys == null)
            {
                IsLoaded = true;
                return true;
            }

            foreach (var key in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(key) || _prefabs.ContainsKey(key))
                    continue;

                var result = await _contentManager.LoadAsync<GameObject>(key, cancellationToken);
                if (result.IsFailure || result.Asset == null || result.Handle == null)
                {
                    Debug.LogError($"[PrefabLoader] Failed to load '{key}': {result.ErrorMessage}");
                    ReleaseAll();
                    return false;
                }

                _prefabs.Add(key, result.Asset);
                _handles.Add(result.Handle);
            }

            IsLoaded = true;
            return true;
        }

        /// <summary>Registers an externally owned prefab without creating a handle.</summary>
        public void RegisterPrefab(string key, GameObject prefab)
        {
            if (!string.IsNullOrWhiteSpace(key) && prefab != null)
                _prefabs[key] = prefab;
        }

        public bool TryGetPrefab(string key, out GameObject prefab)
        {
            if (string.IsNullOrEmpty(key))
            {
                prefab = null;
                return false;
            }

            return _prefabs.TryGetValue(key, out prefab);
        }

        public void ReleaseAll()
        {
            for (var i = 0; i < _handles.Count; i++)
                _contentManager.Release(_handles[i]);

            _handles.Clear();
            _prefabs.Clear();
            IsLoaded = false;
        }

        public void Dispose()
        {
            ReleaseAll();
        }
    }
}
