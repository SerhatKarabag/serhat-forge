using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Serhat.Backend.Core
{
    /// <summary>
    /// Test-only implementation required by the linked, Unity-free monetization client builder.
    /// Production uses the SDK implementation from DefaultImplementations.cs.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new();

        private SystemClock()
        {
        }

        public DateTime UtcNow => DateTime.UtcNow;
        public long TimestampMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

namespace Serhat.Backend.Monetization.Abstractions
{
    /// <summary>
    /// Minimal test contract required by the linked PendingPurchaseStore production source.
    /// The shipped definition lives in IStoreClient.cs and has the same persistence shape.
    /// </summary>
    public sealed class StoreReceipt
    {
        public string Platform { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
        public string ReceiptPayload { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}

namespace UnityEngine
{
    /// <summary>
    /// System.Text.Json-backed test double for Unity's field-based JsonUtility contract.
    /// </summary>
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            IncludeFields = true
        };

        public static string ToJson(object value) =>
            JsonSerializer.Serialize(value, value.GetType(), Options);

        public static T? FromJson<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, Options);
    }

    public static class Debug
    {
        public static void LogWarning(object message)
        {
        }

        public static void LogError(object message)
        {
        }
    }
}
