using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace ScenarioSystem
{
    /// <summary>
    /// Generic proxy for saving/restoring Unity Object references.
    /// Uses a GUID to identify the object in the IUnityObjectSaver.
    /// The value is not serialized (JsonIgnore equivalent), only the guid is persisted.
    /// </summary>
    [Serializable]
    public class ProxyObjectSaver<T> where T : UnityEngine.Object
    {
        [SerializeField]
        [FormerlySerializedAs("guid")]
        private string _guid;

        [NonSerialized]
        private T _value;

        public string Guid
        {
            get => _guid;
            set => _guid = value;
        }

        public T Value
        {
            get => _value;
            set => _value = value;
        }

        public ProxyObjectSaver()
        {
        }

        public ProxyObjectSaver(bool withGuid)
        {
            if (withGuid)
            {
                _guid = System.Guid.NewGuid().ToString();
            }
        }

        /// <summary>
        /// Saves the current value to the saver using the guid as key.
        /// </summary>
        public void Save(IUnityObjectSaver saver)
        {
            if (!string.IsNullOrEmpty(_guid) && _value != null)
            {
                saver.Save(_guid, _value);
            }
        }

        /// <summary>
        /// Restores the value from the saver using the guid as key.
        /// </summary>
        public void Restore(IUnityObjectSaver saver)
        {
            if (!string.IsNullOrEmpty(_guid))
            {
                _value = saver.Restore(_guid) as T;
            }
        }

        /// <summary>
        /// Releases the stored reference from the saver.
        /// </summary>
        public void ReleaseResources(IUnityObjectSaver saver)
        {
            if (!string.IsNullOrEmpty(_guid))
            {
                saver.RemoveStoredObjectByKey(_guid);
            }
        }
    }
}
