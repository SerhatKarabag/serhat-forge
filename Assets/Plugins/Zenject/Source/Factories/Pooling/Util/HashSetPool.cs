using System.Collections.Generic;
using ModestTree;

namespace Zenject
{
    public class HashSetPool<T> : StaticMemoryPool<HashSet<T>>
    {
        static HashSetPool<T> _instance = new HashSetPool<T>();

#if UNITY_EDITOR
        // Unity 6 rejects RuntimeInitializeOnLoadMethod on generic types during player builds.
        // Register each constructed closed pool type with the editor lifecycle instead.
        static HashSetPool()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
#endif

        public HashSetPool()
        {
            OnSpawnMethod = OnSpawned;
            OnDespawnedMethod = OnDespawned;
        }

        public static HashSetPool<T> Instance
        {
            get { return _instance; }
        }
        
#if UNITY_EDITOR
        static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state != UnityEditor.PlayModeStateChange.ExitingEditMode ||
                !UnityEditor.EditorSettings.enterPlayModeOptionsEnabled)
            {
                return;
            }
            
            _instance.Clear();
        }
#endif

        static void OnSpawned(HashSet<T> items)
        {
            Assert.That(items.IsEmpty());
        }

        static void OnDespawned(HashSet<T> items)
        {
            items.Clear();
        }
    }
}
