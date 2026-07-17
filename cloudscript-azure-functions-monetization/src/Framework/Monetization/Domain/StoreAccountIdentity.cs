using System;
using System.Security.Cryptography;
using System.Text;

namespace Serhat.Forge.CloudScript.Framework.Monetization.Domain;

/// <summary>
/// Server-side counterpart of the Unity client's store account identity contract.
/// Values are derived only from the authenticated PlayFab title-player ID.
/// </summary>
public static class StoreAccountIdentity
{
    private const string AppleAccountDomain = "serhat-forge/apple-account/v1:";

    /// <summary>
    /// Creates a deterministic RFC 9562 UUIDv8 for StoreKit appAccountToken.
    /// </summary>
    public static Guid CreateAppleAppAccountToken(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException(
                "A stable authenticated player ID is required.",
                nameof(playerId));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(AppleAccountDomain + playerId));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80); // Deterministic UUID version 8.
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // RFC variant 10xx.

        return Guid.ParseExact(
            string.Create(
                36,
                hash,
                static (chars, bytes) =>
                {
                    const string hex = "0123456789abcdef";
                    var byteIndex = 0;
                    for (var charIndex = 0; charIndex < chars.Length; charIndex++)
                    {
                        if (charIndex is 8 or 13 or 18 or 23)
                        {
                            chars[charIndex] = '-';
                            continue;
                        }

                        var value = bytes[byteIndex];
                        chars[charIndex] = hex[(value >> 4) & 0x0F];
                        chars[++charIndex] = hex[value & 0x0F];
                        byteIndex++;
                    }
                }),
            "D");
    }
}
