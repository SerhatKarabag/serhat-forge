using NUnit.Framework;
using Serhat.Localization.Pluralization;

namespace Serhat.Localization.Tests
{
    [TestFixture]
    public class PluralRuleTests
    {
        [Test]
        public void EnglishRule_ReturnsOne_ForOne()
        {
            var rule = new EnglishPluralRule();
            Assert.AreEqual(PluralCategory.One, rule.GetCategory(1));
        }

        [Test]
        public void EnglishRule_ReturnsOther_ForZero()
        {
            var rule = new EnglishPluralRule();
            Assert.AreEqual(PluralCategory.Other, rule.GetCategory(0));
        }

        [Test]
        public void EnglishRule_ReturnsOther_ForMultiple()
        {
            var rule = new EnglishPluralRule();
            Assert.AreEqual(PluralCategory.Other, rule.GetCategory(2));
            Assert.AreEqual(PluralCategory.Other, rule.GetCategory(5));
            Assert.AreEqual(PluralCategory.Other, rule.GetCategory(100));
        }

        [Test]
        public void TurkishRule_ReturnsOne_ForOne()
        {
            var rule = new TurkishPluralRule();
            Assert.AreEqual(PluralCategory.One, rule.GetCategory(1));
        }

        [Test]
        public void TurkishRule_ReturnsOther_ForOtherNumbers()
        {
            var rule = new TurkishPluralRule();
            Assert.AreEqual(PluralCategory.Other, rule.GetCategory(0));
            Assert.AreEqual(PluralCategory.Other, rule.GetCategory(2));
            Assert.AreEqual(PluralCategory.Other, rule.GetCategory(5));
        }

        [Test]
        public void RussianRule_ReturnsOne_For1_21_31()
        {
            var rule = new RussianPluralRule();
            Assert.AreEqual(PluralCategory.One, rule.GetCategory(1));
            Assert.AreEqual(PluralCategory.One, rule.GetCategory(21));
            Assert.AreEqual(PluralCategory.One, rule.GetCategory(31));
            Assert.AreEqual(PluralCategory.One, rule.GetCategory(101));
        }

        [Test]
        public void RussianRule_ReturnsFew_For2_3_4()
        {
            var rule = new RussianPluralRule();
            Assert.AreEqual(PluralCategory.Few, rule.GetCategory(2));
            Assert.AreEqual(PluralCategory.Few, rule.GetCategory(3));
            Assert.AreEqual(PluralCategory.Few, rule.GetCategory(4));
            Assert.AreEqual(PluralCategory.Few, rule.GetCategory(22));
            Assert.AreEqual(PluralCategory.Few, rule.GetCategory(23));
        }

        [Test]
        public void RussianRule_ReturnsMany_For0_5_11()
        {
            var rule = new RussianPluralRule();
            Assert.AreEqual(PluralCategory.Many, rule.GetCategory(0));
            Assert.AreEqual(PluralCategory.Many, rule.GetCategory(5));
            Assert.AreEqual(PluralCategory.Many, rule.GetCategory(11));
            Assert.AreEqual(PluralCategory.Many, rule.GetCategory(12));
            Assert.AreEqual(PluralCategory.Many, rule.GetCategory(14));
        }

        [Test]
        public void PluralRuleRegistry_ReturnsCorrectRule()
        {
            Assert.IsInstanceOf<EnglishPluralRule>(PluralRuleRegistry.GetRule("en"));
            Assert.IsInstanceOf<TurkishPluralRule>(PluralRuleRegistry.GetRule("tr"));
            Assert.IsInstanceOf<RussianPluralRule>(PluralRuleRegistry.GetRule("ru"));
        }
    }
}
