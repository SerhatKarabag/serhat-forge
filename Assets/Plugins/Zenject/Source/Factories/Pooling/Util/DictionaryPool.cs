using System.Collections.Generic;
using ModestTree;

namespace Zenject
{
    public class DictionaryPool<TKey, TValue> : StaticMemoryPool<Dictionary<TKey, TValue>>
    {
        static DictionaryPool<TKey, TValue> _instance = new DictionaryPool<TKey, TValue>();

#if UNITY_EDITOR
        // Unity 6 rejects RuntimeInitializeOnLoadMethod on generic types during player builds.
        // Register each constructed closed pool type with the editor lifecycle instead.
        static DictionaryPool()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
#endif

        public DictionaryPool()
        {
            OnSpawnMethod = OnSpawned;
            OnDespawnedMethod = OnDespawned;
        }

        public static DictionaryPool<TKey, TValue> Instance
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

        static void OnSpawned(Dictionary<TKey, TValue> items)
        {
            Assert.That(items.IsEmpty());
        }

        static void OnDespawned(Dictionary<TKey, TValue> items)
        {
            items.Clear();
        }
    }
}

