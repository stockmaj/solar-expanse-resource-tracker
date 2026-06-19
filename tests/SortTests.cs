using System.Collections.Generic;
using NUnit.Framework;
using SolarExpanseResourceTracker.Core;

namespace ResourceTrackerTests
{
    [TestFixture]
    internal class SortTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        static StockpileRow MakeStockpileRow(string rdId, double qty, double net = 0, double? daysLeft = null)
            => new StockpileRow { RdId = rdId, RdName = rdId, Qty = qty, NetPerDay = net, DaysLeft = daysLeft };

        static BodyStockpileGroup MakeStockpileBody(string name, params StockpileRow[] rows)
        {
            var g = new BodyStockpileGroup { Name = name };
            g.Rows.AddRange(rows);
            return g;
        }

        static DepositBadge MakeBadge(double size, float factor)
            => new DepositBadge { Size = size, Factor = factor, State = ResourceState.Solid };

        static DepositGroup MakeDepositGroup(string rdId, double totalSize, double effScore,
            double? totalEstDays = null, params DepositBadge[] badges)
        {
            var g = new DepositGroup
            {
                RdId = rdId, RdName = rdId,
                TotalSize    = totalSize,
                EffScore     = effScore,
                TotalEstDays = totalEstDays,
            };
            g.Badges.AddRange(badges);
            return g;
        }

        static BodyDepositGroup MakeDepositBody(string name, params DepositGroup[] groups)
        {
            var g = new BodyDepositGroup { Name = name };
            g.Groups.AddRange(groups);
            foreach (var grp in groups) g.TotalEff += grp.EffScore;
            return g;
        }

        // ── SortStockpiles — body name ─────────────────────────────────────

        [Test]
        public void SortStockpiles_ByBodyName_Alphabetical()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Zebra",  MakeStockpileRow("water", 100)),
                MakeStockpileBody("Alpha",  MakeStockpileRow("water", 200)),
                MakeStockpileBody("Middle", MakeStockpileRow("water", 50)),
            };
            ResourceTrackerSort.SortStockpiles(bodies, "", "qty");
            Assert.That(bodies[0].Name, Is.EqualTo("Alpha"));
            Assert.That(bodies[1].Name, Is.EqualTo("Middle"));
            Assert.That(bodies[2].Name, Is.EqualTo("Zebra"));
        }

        [Test]
        public void SortStockpiles_NullSortRes_Alphabetical()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Zzz", MakeStockpileRow("water", 100)),
                MakeStockpileBody("Aaa", MakeStockpileRow("water", 200)),
            };
            ResourceTrackerSort.SortStockpiles(bodies, null, "qty");
            Assert.That(bodies[0].Name, Is.EqualTo("Aaa"));
        }

        // ── SortStockpiles — by resource qty ──────────────────────────────

        [Test]
        public void SortStockpiles_ByResourceQty_HigherFirst()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Low",  MakeStockpileRow("water", 100)),
                MakeStockpileBody("High", MakeStockpileRow("water", 500)),
                MakeStockpileBody("Mid",  MakeStockpileRow("water", 300)),
            };
            ResourceTrackerSort.SortStockpiles(bodies, "water", "qty");
            Assert.That(bodies[0].Name, Is.EqualTo("High"));
            Assert.That(bodies[1].Name, Is.EqualTo("Mid"));
            Assert.That(bodies[2].Name, Is.EqualTo("Low"));
        }

        [Test]
        public void SortStockpiles_ByResourceQty_MissingResourceToBottom()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("NoWater", MakeStockpileRow("iron",  200)),
                MakeStockpileBody("Mars",    MakeStockpileRow("water", 100)),
            };
            ResourceTrackerSort.SortStockpiles(bodies, "water", "qty");
            Assert.That(bodies[0].Name, Is.EqualTo("Mars"));
            Assert.That(bodies[1].Name, Is.EqualTo("NoWater"));
        }

        [Test]
        public void SortStockpiles_ByResourceQty_TieBreakAlphabetical()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Zzz", MakeStockpileRow("water", 100)),
                MakeStockpileBody("Aaa", MakeStockpileRow("water", 100)),
            };
            ResourceTrackerSort.SortStockpiles(bodies, "water", "qty");
            Assert.That(bodies[0].Name, Is.EqualTo("Aaa"));
            Assert.That(bodies[1].Name, Is.EqualTo("Zzz"));
        }

        // ── SortStockpiles — by net/day ────────────────────────────────────

        [Test]
        public void SortStockpiles_ByNetPerDay_HigherFirst()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("LowNet",  MakeStockpileRow("water", 100, net: 10)),
                MakeStockpileBody("HighNet", MakeStockpileRow("water", 100, net: 50)),
            };
            ResourceTrackerSort.SortStockpiles(bodies, "water", "net");
            Assert.That(bodies[0].Name, Is.EqualTo("HighNet"));
        }

        // ── SortStockpiles — by lasts ──────────────────────────────────────

        [Test]
        public void SortStockpiles_ByLasts_HigherFirst()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Short", MakeStockpileRow("water", 100, daysLeft: 10)),
                MakeStockpileBody("Long",  MakeStockpileRow("water", 100, daysLeft: 200)),
            };
            ResourceTrackerSort.SortStockpiles(bodies, "water", "lasts");
            Assert.That(bodies[0].Name, Is.EqualTo("Long"));
        }

        [Test]
        public void SortStockpiles_ByLasts_NullDaysLeftCountsAsMaxValue()
        {
            // Null daysLeft (not depleting) should sort above a body with a finite lasts value
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Finite",   MakeStockpileRow("water", 100, daysLeft: 999)),
                MakeStockpileBody("Infinite", MakeStockpileRow("water", 100, daysLeft: null)),
            };
            ResourceTrackerSort.SortStockpiles(bodies, "water", "lasts");
            Assert.That(bodies[0].Name, Is.EqualTo("Infinite"));
        }

        // ── StockSortValue ─────────────────────────────────────────────────

        [Test]
        public void StockSortValue_QtyQual_ReturnsQty()
        {
            var body = MakeStockpileBody("Mars", MakeStockpileRow("water", 300));
            Assert.That(ResourceTrackerSort.StockSortValue(body, "water", "qty"), Is.EqualTo(300));
        }

        [Test]
        public void StockSortValue_NetQual_ReturnsNetPerDay()
        {
            var body = MakeStockpileBody("Mars", MakeStockpileRow("water", 100, net: 42));
            Assert.That(ResourceTrackerSort.StockSortValue(body, "water", "net"), Is.EqualTo(42));
        }

        [Test]
        public void StockSortValue_LastsQual_ReturnsDaysLeft()
        {
            var body = MakeStockpileBody("Mars", MakeStockpileRow("water", 100, daysLeft: 77));
            Assert.That(ResourceTrackerSort.StockSortValue(body, "water", "lasts"), Is.EqualTo(77));
        }

        [Test]
        public void StockSortValue_LastsQualNullDays_ReturnsMaxValue()
        {
            var body = MakeStockpileBody("Mars", MakeStockpileRow("water", 100, daysLeft: null));
            Assert.That(ResourceTrackerSort.StockSortValue(body, "water", "lasts"), Is.EqualTo(double.MaxValue));
        }

        [Test]
        public void StockSortValue_MissingResource_ReturnsMinValue()
        {
            var body = MakeStockpileBody("Mars", MakeStockpileRow("iron", 100));
            Assert.That(ResourceTrackerSort.StockSortValue(body, "water", "qty"), Is.EqualTo(double.MinValue));
        }

        // ── SortDeposits — body name ───────────────────────────────────────

        [Test]
        public void SortDeposits_ByBodyName_Alphabetical()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Zzz", MakeDepositGroup("water", 500, 250)),
                MakeDepositBody("Aaa", MakeDepositGroup("water", 300, 150)),
            };
            ResourceTrackerSort.SortDeposits(bodies, "", "total");
            Assert.That(bodies[0].Name, Is.EqualTo("Aaa"));
            Assert.That(bodies[1].Name, Is.EqualTo("Zzz"));
        }

        // ── SortDeposits — by total ────────────────────────────────────────

        [Test]
        public void SortDeposits_ByTotal_HigherFirst()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Small", MakeDepositGroup("water", 100, 50)),
                MakeDepositBody("Large", MakeDepositGroup("water", 900, 450)),
            };
            ResourceTrackerSort.SortDeposits(bodies, "water", "total");
            Assert.That(bodies[0].Name, Is.EqualTo("Large"));
        }

        [Test]
        public void SortDeposits_ByTotal_MissingResourceToBottom()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("NoWater", MakeDepositGroup("iron",  500, 250)),
                MakeDepositBody("Mars",    MakeDepositGroup("water", 100,  50)),
            };
            ResourceTrackerSort.SortDeposits(bodies, "water", "total");
            Assert.That(bodies[0].Name, Is.EqualTo("Mars"));
            Assert.That(bodies[1].Name, Is.EqualTo("NoWater"));
        }

        // ── SortDeposits — by eff ──────────────────────────────────────────

        [Test]
        public void SortDeposits_ByEff_HigherFirst()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("LowEff",  MakeDepositGroup("water", 1000,  50)),
                MakeDepositBody("HighEff", MakeDepositGroup("water", 1000, 700)),
            };
            ResourceTrackerSort.SortDeposits(bodies, "water", "eff");
            Assert.That(bodies[0].Name, Is.EqualTo("HighEff"));
        }

        // ── SortDeposits — by bestquality ─────────────────────────────────

        [Test]
        public void SortDeposits_ByBestQuality_HigherFactorFirst()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("LowQ",  MakeDepositGroup("water", 500, 100, null, MakeBadge(500, 0.2f))),
                MakeDepositBody("HighQ", MakeDepositGroup("water", 500, 400, null, MakeBadge(500, 0.8f))),
            };
            ResourceTrackerSort.SortDeposits(bodies, "water", "bestquality");
            Assert.That(bodies[0].Name, Is.EqualTo("HighQ"));
        }

        // ── SortDeposits — by lasts ────────────────────────────────────────

        [Test]
        public void SortDeposits_ByLasts_HigherFirst()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Short", MakeDepositGroup("water", 500, 250, totalEstDays: 30)),
                MakeDepositBody("Long",  MakeDepositGroup("water", 500, 250, totalEstDays: 365)),
            };
            ResourceTrackerSort.SortDeposits(bodies, "water", "lasts");
            Assert.That(bodies[0].Name, Is.EqualTo("Long"));
        }

        // ── DepositSortValue ───────────────────────────────────────────────

        [Test]
        public void DepositSortValue_TotalQual_ReturnsTotalSize()
        {
            var body = MakeDepositBody("Mars", MakeDepositGroup("water", 999, 500));
            Assert.That(ResourceTrackerSort.DepositSortValue(body, "water", "total"), Is.EqualTo(999));
        }

        [Test]
        public void DepositSortValue_EffQual_ReturnsEffScore()
        {
            var body = MakeDepositBody("Mars", MakeDepositGroup("water", 999, 500));
            Assert.That(ResourceTrackerSort.DepositSortValue(body, "water", "eff"), Is.EqualTo(500));
        }

        [Test]
        public void DepositSortValue_BestQualityQual_ReturnsFirstBadgeFactor()
        {
            var grp = MakeDepositGroup("water", 500, 250, null, MakeBadge(500, 0.75f));
            var body = MakeDepositBody("Mars", grp);
            Assert.That(ResourceTrackerSort.DepositSortValue(body, "water", "bestquality"),
                Is.EqualTo(0.75f).Within(1e-6));
        }

        [Test]
        public void DepositSortValue_BestQualityQual_NoBadges_ReturnsZero()
        {
            var grp = MakeDepositGroup("water", 500, 250);
            var body = MakeDepositBody("Mars", grp);
            Assert.That(ResourceTrackerSort.DepositSortValue(body, "water", "bestquality"), Is.EqualTo(0));
        }

        [Test]
        public void DepositSortValue_LastsQual_ReturnsTotalEstDays()
        {
            var grp = MakeDepositGroup("water", 500, 250, totalEstDays: 120);
            var body = MakeDepositBody("Mars", grp);
            Assert.That(ResourceTrackerSort.DepositSortValue(body, "water", "lasts"), Is.EqualTo(120));
        }

        [Test]
        public void DepositSortValue_LastsQual_NullEstDays_ReturnsMaxValue()
        {
            var grp = MakeDepositGroup("water", 500, 250, totalEstDays: null);
            var body = MakeDepositBody("Mars", grp);
            Assert.That(ResourceTrackerSort.DepositSortValue(body, "water", "lasts"), Is.EqualTo(double.MaxValue));
        }

        [Test]
        public void DepositSortValue_MissingResource_ReturnsMinValue()
        {
            var body = MakeDepositBody("Mars", MakeDepositGroup("iron", 500, 250));
            Assert.That(ResourceTrackerSort.DepositSortValue(body, "water", "total"), Is.EqualTo(double.MinValue));
        }

        // ── Inner-row sorting ──────────────────────────────────────────────

        [Test]
        public void SortStockpiles_RowsSortedByQtyDescWithinBody()
        {
            var body = MakeStockpileBody("Mars",
                MakeStockpileRow("iron",  50),
                MakeStockpileRow("water", 200),
                MakeStockpileRow("gas",   100));
            var bodies = new List<BodyStockpileGroup> { body };
            ResourceTrackerSort.SortStockpiles(bodies, "", "qty");
            Assert.That(bodies[0].Rows[0].RdId, Is.EqualTo("gas"));
            Assert.That(bodies[0].Rows[1].RdId, Is.EqualTo("iron"));
            Assert.That(bodies[0].Rows[2].RdId, Is.EqualTo("water"));
        }

        [Test]
        public void SortDeposits_GroupsSortedByEffDescWithinBody()
        {
            var body = MakeDepositBody("Mars",
                MakeDepositGroup("iron",  500,  50),
                MakeDepositGroup("water", 300, 200),
                MakeDepositGroup("gas",   400, 100));
            var bodies = new List<BodyDepositGroup> { body };
            ResourceTrackerSort.SortDeposits(bodies, "", "total");
            Assert.That(bodies[0].Groups[0].RdId, Is.EqualTo("gas"));
            Assert.That(bodies[0].Groups[1].RdId, Is.EqualTo("iron"));
            Assert.That(bodies[0].Groups[2].RdId, Is.EqualTo("water"));
        }
    }
}
