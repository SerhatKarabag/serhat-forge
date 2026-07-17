using NUnit.Framework;
using System.Collections.Generic;
using Serhat.Localization.Data;
using Serhat.Localization.Pluralization;

namespace Serhat.Localization.Tests
{
    [TestFixture]
    public class StringTableTests
    {
        [Test]
        public void StringTable_StoresAndRetrievesStrings()
        {
            var table = new StringTable("en");
            table.SetString("test.key", "Test Value");

            Assert.AreEqual("Test Value", table.GetString("test.key"));
        }

        [Test]
        public void StringTable_OverwritesExistingKey()
        {
            var table = new StringTable("en");
            table.SetString("test.key", "First");
            table.SetString("test.key", "Second");

            Assert.AreEqual("Second", table.GetString("test.key"));
        }

        [Test]
        public void StringTable_ContainsKey_ReturnsCorrectly()
        {
            var table = new StringTable("en");
            table.SetString("exists", "value");

            Assert.IsTrue(table.ContainsKey("exists"));
            Assert.IsFalse(table.ContainsKey("doesnt.exist"));
        }

        [Test]
        public void StringEntry_Simple_IsNotPluralEntry()
        {
            var entry = new StringEntry("Simple value");
            Assert.IsFalse(entry.IsPluralEntry);
            Assert.AreEqual("Simple value", entry.Value);
        }

        [Test]
        public void StringEntry_Plural_IsPluralEntry()
        {
            var pluralForms = new Dictionary<PluralCategory, string>
            {
                { PluralCategory.One, "1 item" },
                { PluralCategory.Other, "{0} items" }
            };

            var entry = new StringEntry(pluralForms);

            Assert.IsTrue(entry.IsPluralEntry);
            Assert.AreEqual("1 item", entry.GetPluralForm(PluralCategory.One));
            Assert.AreEqual("{0} items", entry.GetPluralForm(PluralCategory.Other));
        }

        [Test]
        public void StringEntry_GetPluralForm_FallsBackToOther()
        {
            var pluralForms = new Dictionary<PluralCategory, string>
            {
                { PluralCategory.Other, "{0} items" }
            };

            var entry = new StringEntry(pluralForms);

            // "Few" not defined, should fall back to "Other"
            Assert.AreEqual("{0} items", entry.GetPluralForm(PluralCategory.Few));
        }
    }
}
