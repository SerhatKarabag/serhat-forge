using NUnit.Framework;

namespace Serhat.Localization.Tests
{
    [TestFixture]
    public class LocaleTests
    {
        [Test]
        public void Locale_ParsesSimpleCode()
        {
            var locale = new Locale("en");
            Assert.AreEqual("en", locale.Code);
            Assert.AreEqual("en", locale.Language);
            Assert.AreEqual("", locale.Region);
        }

        [Test]
        public void Locale_ParsesRegionalCode()
        {
            var locale = new Locale("en-US");
            Assert.AreEqual("en-us", locale.Code);
            Assert.AreEqual("en", locale.Language);
            Assert.AreEqual("us", locale.Region);
        }

        [Test]
        public void Locale_NormalizesToLowercase()
        {
            var locale = new Locale("EN-US");
            Assert.AreEqual("en-us", locale.Code);
        }

        [Test]
        public void Locale_Equals_ComparesCorrectly()
        {
            var locale1 = new Locale("en-US");
            var locale2 = new Locale("en-us");
            var locale3 = new Locale("tr");

            Assert.IsTrue(locale1.Equals(locale2));
            Assert.IsFalse(locale1.Equals(locale3));
        }

        [Test]
        public void Locale_HasRegion_ReturnsCorrectly()
        {
            var localeWithRegion = new Locale("en-US");
            var localeWithoutRegion = new Locale("en");

            Assert.IsTrue(localeWithRegion.HasRegion);
            Assert.IsFalse(localeWithoutRegion.HasRegion);
        }

        [Test]
        public void Locale_GetLanguageLocale_ReturnsBaseLanguage()
        {
            var locale = new Locale("en-US");
            var language = locale.GetLanguageLocale();

            Assert.AreEqual("en", language.Code);
        }
    }
}
