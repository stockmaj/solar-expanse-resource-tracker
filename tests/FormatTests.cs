using System.Collections.Generic;
using NUnit.Framework;
using SolarExpanseResourceTracker.Core;

namespace ResourceTrackerTests
{
    [TestFixture]
    internal class FormatTests
    {
        // ── FormatQty ─────────────────────────────────────────────────────────

        [Test]
        public void FormatQty_BelowThousand_ShowsT()
        {
            Assert.That(ResourceTrackerFormat.FormatQty(500), Is.EqualTo("500T"));
        }

        [Test]
        public void FormatQty_AtThousand_ShowsKT()
        {
            Assert.That(ResourceTrackerFormat.FormatQty(1_000), Is.EqualTo("1KT"));
        }

        [Test]
        public void FormatQty_TwoThousand_ShowsKT()
        {
            Assert.That(ResourceTrackerFormat.FormatQty(2_000), Is.EqualTo("2KT"));
        }

        [Test]
        public void FormatQty_AtMillion_ShowsMT()
        {
            Assert.That(ResourceTrackerFormat.FormatQty(1_000_000), Is.EqualTo("1MT"));
        }

        [Test]
        public void FormatQty_ThreeMillion_ShowsMT()
        {
            Assert.That(ResourceTrackerFormat.FormatQty(3_000_000), Is.EqualTo("3MT"));
        }

        // ── FormatKT ──────────────────────────────────────────────────────────

        [Test]
        public void FormatKT_BelowThousand_ShowsT()
        {
            Assert.That(ResourceTrackerFormat.FormatKT(500), Is.EqualTo("500.0T"));
        }

        [Test]
        public void FormatKT_TwoThousand_ShowsKT()
        {
            Assert.That(ResourceTrackerFormat.FormatKT(2_000), Is.EqualTo("2.0KT"));
        }

        [Test]
        public void FormatKT_AtMillion_ShowsMTTwoDecimals()
        {
            Assert.That(ResourceTrackerFormat.FormatKT(1_000_000), Is.EqualTo("1.00MT"));
        }

        [Test]
        public void FormatKT_TwoAndHalfMillion_ShowsMTTwoDecimals()
        {
            Assert.That(ResourceTrackerFormat.FormatKT(2_500_000), Is.EqualTo("2.50MT"));
        }

        // ── FormatFlow ────────────────────────────────────────────────────────

        [Test]
        public void FormatFlow_BelowThousand_ShowsNoUnit()
        {
            Assert.That(ResourceTrackerFormat.FormatFlow(500), Is.EqualTo("500.0"));
        }

        [Test]
        public void FormatFlow_AtThousand_ShowsKT()
        {
            Assert.That(ResourceTrackerFormat.FormatFlow(1_000), Is.EqualTo("1.0KT"));
        }

        [Test]
        public void FormatFlow_TwoThousand_ShowsKT()
        {
            Assert.That(ResourceTrackerFormat.FormatFlow(2_000), Is.EqualTo("2.0KT"));
        }

        // ── FormatDays ────────────────────────────────────────────────────────

        [Test]
        public void FormatDays_BelowThirty_ShowsDays()
        {
            Assert.That(ResourceTrackerFormat.FormatDays(10), Is.EqualTo("10.0d"));
        }

        [Test]
        public void FormatDays_FortyFive_ShowsMonths()
        {
            Assert.That(ResourceTrackerFormat.FormatDays(45), Is.EqualTo("1.5mo"));
        }

        [Test]
        public void FormatDays_ThirtyDays_ShowsMonths()
        {
            Assert.That(ResourceTrackerFormat.FormatDays(30), Is.EqualTo("1.0mo"));
        }

        [Test]
        public void FormatDays_EightHundredDays_ShowsYears()
        {
            // 800 / 365 = 2.19...y, rounds to 2.2y
            Assert.That(ResourceTrackerFormat.FormatDays(800), Is.EqualTo("2.2y"));
        }

        [Test]
        public void FormatDays_ExactlyTwoYears_ShowsYears()
        {
            // 365 * 2 = 730
            Assert.That(ResourceTrackerFormat.FormatDays(730), Is.EqualTo("2.0y"));
        }

        [Test]
        public void FormatDays_ExactlyThousandYears_ShowsOver1000y()
        {
            Assert.That(ResourceTrackerFormat.FormatDays(1000 * 365), Is.EqualTo("> 1000 y"));
        }

        [Test]
        public void FormatDays_MassiveValue_ShowsOver1000y()
        {
            Assert.That(ResourceTrackerFormat.FormatDays(687823646.2 * 365), Is.EqualTo("> 1000 y"));
        }

        [Test]
        public void FormatDays_JustUnderTwoYears_ShowsMonths()
        {
            // 729 days / 30 = 24.3mo
            Assert.That(ResourceTrackerFormat.FormatDays(729), Is.EqualTo("24.3mo"));
        }

        // ── FormatState ───────────────────────────────────────────────────────

        [Test]
        public void FormatState_Solid_ReturnsSol()
        {
            Assert.That(ResourceTrackerFormat.FormatState(ResourceState.Solid), Is.EqualTo("Sol"));
        }

        [Test]
        public void FormatState_Liquid_ReturnsLiq()
        {
            Assert.That(ResourceTrackerFormat.FormatState(ResourceState.Liquid), Is.EqualTo("Liq"));
        }

        [Test]
        public void FormatState_Gas_ReturnsGas()
        {
            Assert.That(ResourceTrackerFormat.FormatState(ResourceState.Gas), Is.EqualTo("Gas"));
        }

        [Test]
        public void FormatState_Underground_ReturnsUnd()
        {
            Assert.That(ResourceTrackerFormat.FormatState(ResourceState.Underground), Is.EqualTo("Und"));
        }

        // ── QualityColorHex ───────────────────────────────────────────────────

        [Test]
        public void QualityColorHex_HighFactor_ReturnsGreen()
        {
            Assert.That(ResourceTrackerFormat.QualityColorHex(0.8f), Is.EqualTo("#4CAF50"));
        }

        [Test]
        public void QualityColorHex_AtSeventyPercent_ReturnsGreen()
        {
            Assert.That(ResourceTrackerFormat.QualityColorHex(0.70f), Is.EqualTo("#4CAF50"));
        }

        [Test]
        public void QualityColorHex_MidFactor_ReturnsYellow()
        {
            Assert.That(ResourceTrackerFormat.QualityColorHex(0.5f), Is.EqualTo("#FFC107"));
        }

        [Test]
        public void QualityColorHex_AtFortyPercent_ReturnsYellow()
        {
            Assert.That(ResourceTrackerFormat.QualityColorHex(0.40f), Is.EqualTo("#FFC107"));
        }

        [Test]
        public void QualityColorHex_LowFactor_ReturnsRed()
        {
            Assert.That(ResourceTrackerFormat.QualityColorHex(0.1f), Is.EqualTo("#F44336"));
        }

        [Test]
        public void QualityColorHex_JustBelowForty_ReturnsRed()
        {
            Assert.That(ResourceTrackerFormat.QualityColorHex(0.39f), Is.EqualTo("#F44336"));
        }

        // ── RowKey ────────────────────────────────────────────────────────────

        [Test]
        public void RowKey_CombinesWithSeparator()
        {
            Assert.That(ResourceTrackerFormat.RowKey("Mars", "id_resource_water"),
                Is.EqualTo("Mars\x1fid_resource_water"));
        }

        [Test]
        public void RowKey_DifferentBodies_ProduceDifferentKeys()
        {
            string k1 = ResourceTrackerFormat.RowKey("Mars",  "id_resource_water");
            string k2 = ResourceTrackerFormat.RowKey("Earth", "id_resource_water");
            Assert.That(k1, Is.Not.EqualTo(k2));
        }

        [Test]
        public void RowKey_DifferentResources_ProduceDifferentKeys()
        {
            string k1 = ResourceTrackerFormat.RowKey("Mars", "id_resource_water");
            string k2 = ResourceTrackerFormat.RowKey("Mars", "id_resource_iron");
            Assert.That(k1, Is.Not.EqualTo(k2));
        }
    }
}
