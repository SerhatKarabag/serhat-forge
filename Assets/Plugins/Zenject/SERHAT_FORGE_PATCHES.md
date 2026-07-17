# Serhat Forge Extenject patches

The vendored source is based on Extenject 9.2.1 commit
`8d8cc2ca14189b3efe91e19f41d1ae89cf44bf8a`.

Unity 6 reports `RuntimeInitializeOnLoadMethod` declarations on generic types as
invalid during player builds. The generic `ListPool<T>`, `HashSetPool<T>`, and
`DictionaryPool<TKey, TValue>` reset hooks therefore use
`EditorApplication.playModeStateChanged` instead. This preserves their
no-domain-reload editor cleanup without shipping invalid runtime initialization
methods or producing player-build warnings.

The obsolete `Object.FindObjectsOfType<T>()` calls in `UnityUtil` and
`ProjectContext` use `Object.FindObjectsByType<T>(FindObjectsSortMode.InstanceID)`
under Unity 6. This keeps the upstream active-object filtering and deterministic
Instance ID ordering while removing obsolete-API compiler warnings.
