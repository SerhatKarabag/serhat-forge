#nullable enable
using Serhat.Backend.Core;
using PlayFab.Json;

namespace Serhat.Backend.PlayFab
{
    /// <summary>
    /// Serializer backed by PlayFab's SimpleJson implementation.
    /// Handles property-based POCO models used by transport envelopes.
    /// </summary>
    public sealed class PlayFabSimpleJsonSerializer : ISerializer
    {
        private static readonly SimpleJsonInstance Json = new();

        public string Serialize<T>(T value)
        {
            if (value == null)
                return "null";

            return Json.SerializeObject(value);
        }

        public T? Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "null")
                return default;

            try
            {
                return Json.DeserializeObject<T>(json);
            }
            catch
            {
                return default;
            }
        }
    }
}
