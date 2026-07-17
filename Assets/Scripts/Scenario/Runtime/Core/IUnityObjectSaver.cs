using UnityEngine;

namespace ScenarioSystem
{
    /// <summary>
    /// Interface for saving and restoring Unity Object references.
    /// Used by commands to persist references to scene objects.
    /// </summary>
    public interface IUnityObjectSaver
    {
        /// <summary>
        /// Saves a Unity Object with the given key.
        /// </summary>
        void Save(string key, Object savedObject);

        /// <summary>
        /// Restores a Unity Object by its key.
        /// </summary>
        Object Restore(string key);

        /// <summary>
        /// Removes a stored object by its key.
        /// </summary>
        void RemoveStoredObjectByKey(string key);
    }
}
