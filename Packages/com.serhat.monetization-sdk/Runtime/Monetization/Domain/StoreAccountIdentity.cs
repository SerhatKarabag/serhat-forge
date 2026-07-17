#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

namespace Serhat.Backend.Monetization.Domain
{
    /// <summary>
    /// Creates stable, non-PII store account identifiers shared by client and backend policy.
    /// </summary>
    public static class StoreAccountIdentity
    {
        private const string GoogleAccountDomain = "serhat-forge/google-account/v1:";
        private const string AppleAccountDomain = "serhat-forge/apple-account/v1:";

        /// <summary>
        /// Produces the 64-character value that should be supplied to Google Play before a
        /// purchase and compared with the authenticated backend player during verification.
        /// </summary>
        public static string CreateGoogleObfuscatedAccountId(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                throw new ArgumentException("A stable authenticated player ID is required.", nameof(playerId));
            }

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(
                Encoding.UTF8.GetBytes(GoogleAccountDomain + playerId));
            var result = new StringBuilder(hash.Length * 2);
            for (var index = 0; index < hash.Length; index++)
            {
                result.Append(hash[index].ToString("X2"));
            }

            return result.ToString();
        }

        /// <summary>
        /// Produces a deterministic RFC 9562 UUIDv8 that should be supplied to StoreKit as the
        /// appAccountToken and compared with the authenticated backend player during verification.
        /// </summary>
        public static Guid CreateAppleAppAccountToken(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                throw new ArgumentException(
                    "A stable authenticated player ID is required.",
                    nameof(playerId));
            }

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(
                Encoding.UTF8.GetBytes(AppleAccountDomain + playerId));
            hash[6] = (byte)((hash[6] & 0x0F) | 0x80); // Deterministic UUID version 8.
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // RFC variant 10xx.

            var builder = new StringBuilder(36);
            for (var index = 0; index < 16; index++)
            {
                if (index is 4 or 6 or 8 or 10)
                {
                    builder.Append('-');
                }

                builder.Append(hash[index].ToString("x2"));
            }

            return Guid.ParseExact(builder.ToString(), "D");
        }
    }
}
