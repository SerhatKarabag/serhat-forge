using System.Collections.Generic;

namespace Zenject
{
    public class ListPool<T> : StaticMemoryPool<List<T>>
    {
        static ListPool<T> _instance = new ListPool<T>();

#if UNITY_EDITOR
        // Unity 6 rejects RuntimeInitializeOnLoadMethod on generic types during player builds.
        // Register each constructed closed pool type with the editor lifecycle instead.
        static ListPool()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
#endif

        public ListPool()
        {
            OnDespawnedMethod = OnDespawned;
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

        public static ListPool<T> Instance
        {
            get { return _instance; }
        }

        void OnDespawned(List<T> list)
        {
            list.Clear();
        }
    }
}
