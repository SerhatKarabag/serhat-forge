#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using Serhat.Backend.Monetization.Abstractions;
using Serhat.Backend.Monetization.Domain;

namespace Serhat.Forge.Tests.EditMode
{
    [TestFixture]
    public sealed class StoreAccountIdentityTests
    {
        [Test]
        public void CreateGoogleObfuscatedAccountId_SamePlayer_ReturnsStableUppercaseSha256()
        {
            var first = StoreAccountIdentity.CreateGoogleObfuscatedAccountId("player-123");
            var second = StoreAccountIdentity.CreateGoogleObfuscatedAccountId("player-123");

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(first, Does.Match("^[0-9A-F]{64}$"));
        }

        [Test]
        public void CreateGoogleObfuscatedAccountId_DifferentPlayers_ReturnsDifferentValues()
        {
            var first = StoreAccountIdentity.CreateGoogleObfuscatedAccountId("player-123");
            var second = StoreAccountIdentity.CreateGoogleObfuscatedAccountId("player-456");

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void CreateGoogleObfuscatedAccountId_MissingPlayer_Throws(string? playerId)
        {
            Assert.That(
                () => StoreAccountIdentity.CreateGoogleObfuscatedAccountId(playerId!),
                Throws.ArgumentException);
        }

        [Test]
        public void CreateAppleAppAccountToken_SamePlayer_ReturnsStableDeterministicUuidV8()
        {
            var first = StoreAccountIdentity.CreateAppleAppAccountToken("player-123");
            var second = StoreAccountIdentity.CreateAppleAppAccountToken("player-123");
            var text = first.ToString("D");

            Assert.That(first, Is.EqualTo(second));
            Assert.That(text, Is.EqualTo("70fbf6e6-6dda-8a9a-a008-b37b5b4133ac"));
            Assert.That(text[14], Is.EqualTo('8'));
            Assert.That("89ab", Does.Contain(text[19]));
        }

        [Test]
        public void CreateAppleAppAccountToken_DifferentPlayers_ReturnsDifferentValues()
        {
            var first = StoreAccountIdentity.CreateAppleAppAccountToken("player-123");
            var second = StoreAccountIdentity.CreateAppleAppAccountToken("player-456");

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void CreateAppleAppAccountToken_MissingPlayer_Throws(string? playerId)
        {
            Assert.That(
                () => StoreAccountIdentity.CreateAppleAppAccountToken(playerId!),
                Throws.ArgumentException);
        }

        [Test]
        public void StoreAccountBinding_ExposesAppleGuidContract()
        {
            var method = typeof(IStoreAccountBinding).GetMethod(
                nameof(IStoreAccountBinding.SetAppleAppAccountToken));

            Assert.That(method, Is.Not.Null);
            Assert.That(
                method!.GetParameters().Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[] { typeof(Guid) }));
        }
    }
}
