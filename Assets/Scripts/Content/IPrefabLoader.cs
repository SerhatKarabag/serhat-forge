using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Forge.Content
{
    /// <summary>
    /// Owns boot-preloaded prefab handles and exposes synchronous lookups afterward.
    /// </summary>
    public interface IPrefabLoader
    {
        bool IsLoaded { get; }

        Task<bool> PreloadAsync(
            IEnumerable<string> keys,
            CancellationToken cancellationToken = default);

        bool TryGetPrefab(string key, out GameObject prefab);

        void ReleaseAll();
    }
}
