using System.Collections.Generic;
using NUnit.Framework;
using SolarExpanseResourceTracker.Core;

namespace ResourceTrackerTests
{
    [TestFixture]
    internal class FilterTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        static StockpileRow MakeStockpileRow(string rdId, double qty)
            => new StockpileRow { RdId = rdId, RdName = rdId, Qty = qty, State = ResourceState.Solid };

        static BodyStockpileGroup MakeStockpileBody(string name, params StockpileRow[] rows)
        {
            var g = new BodyStockpileGroup { Name = name };
            g.Rows.AddRange(rows);
            return g;
        }

        static DepositBadge MakeBadge(double size, float factor)
            => new DepositBadge { Size = size, Factor = factor, State = ResourceState.Solid };

        static DepositGroup MakeDepositGroup(string rdId, params DepositBadge[] badges)
        {
            var g = new DepositGroup { RdId = rdId, RdName = rdId };
            g.Badges.AddRange(badges);
            g.TotalSize = 0;
            g.EffScore  = 0;
            foreach (var b in badges) { g.TotalSize += b.Size; g.EffScore += b.Size * b.Factor; }
            return g;
        }

        static BodyDepositGroup MakeDepositBody(string name, params DepositGroup[] groups)
        {
            var g = new BodyDepositGroup { Name = name };
            g.Groups.AddRange(groups);
            foreach (var grp in groups) g.TotalEff += grp.EffScore;
            return g;
        }

        // ── FilterCombo (stockpiles) ──────────────────────────────────────────

        [Test]
        public void FilterCombo_EmptyActive_ReturnsAll()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Mars",  MakeStockpileRow("water", 100)),
                MakeStockpileBody("Earth", MakeStockpileRow("water", 200)),
            };
            var result = ResourceTrackerFilter.FilterCombo(bodies, new HashSet<string>());
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void FilterCombo_NullActive_ReturnsAll()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Mars", MakeStockpileRow("water", 100)),
            };
            var result = ResourceTrackerFilter.FilterCombo(bodies, null);
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void FilterCombo_ActiveResource_IncludesMatchingBody()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Mars",  MakeStockpileRow("water", 100), MakeStockpileRow("iron", 50)),
                MakeStockpileBody("Earth", MakeStockpileRow("iron",  200)),
            };
            var result = ResourceTrackerFilter.FilterCombo(bodies, new HashSet<string> { "water" });
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Name, Is.EqualTo("Mars"));
        }

        [Test]
        public void FilterCombo_ActiveResourceNotPresent_ReturnsEmpty()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Mars", MakeStockpileRow("iron", 100)),
            };
            var result = ResourceTrackerFilter.FilterCombo(bodies, new HashSet<string> { "water" });
            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void FilterCombo_ActiveResourceZeroQty_ExcludesBody()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Mars", MakeStockpileRow("water", 0)),
            };
            var result = ResourceTrackerFilter.FilterCombo(bodies, new HashSet<string> { "water" });
            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void FilterCombo_MultipleActive_BodyMustHaveAll()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Mars",  MakeStockpileRow("water", 100), MakeStockpileRow("iron", 50)),
                MakeStockpileBody("Earth", MakeStockpileRow("water", 200)),
            };
            var result = ResourceTrackerFilter.FilterCombo(bodies, new HashSet<string> { "water", "iron" });
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Name, Is.EqualTo("Mars"));
        }

        // ── FilterHidden (stockpiles) ─────────────────────────────────────────

        [Test]
        public void FilterHidden_EmptyHidden_ReturnsAll()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Mars", MakeStockpileRow("water", 100)),
            };
            var result = ResourceTrackerFilter.FilterHidden(bodies, new HashSet<string>());
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void FilterHidden_HideWater_WaterRowsRemoved()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Mars", MakeStockpileRow("water", 100), MakeStockpileRow("iron", 50)),
            };
            var result = ResourceTrackerFilter.FilterHidden(bodies, new HashSet<string> { "water" });
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Rows.Count, Is.EqualTo(1));
            Assert.That(result[0].Rows[0].RdId, Is.EqualTo("iron"));
        }

        [Test]
        public void FilterHidden_AllRowsHidden_BodyRemoved()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Mars", MakeStockpileRow("water", 100)),
            };
            var result = ResourceTrackerFilter.FilterHidden(bodies, new HashSet<string> { "water" });
            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void FilterHidden_NullHidden_ReturnsAll()
        {
            var bodies = new List<BodyStockpileGroup>
            {
                MakeStockpileBody("Mars", MakeStockpileRow("water", 100)),
            };
            var result = ResourceTrackerFilter.FilterHidden(bodies, null);
            Assert.That(result.Count, Is.EqualTo(1));
        }

        // ── FilterComboDeposits (per-resource qty/quality thresholds) ────────

        static System.Collections.Generic.Dictionary<string, float> NoThresh()
            => new System.Collections.Generic.Dictionary<string, float>();

        static System.Collections.Generic.Dictionary<string, float> Thresh(string id, float v)
            => new System.Collections.Generic.Dictionary<string, float> { [id] = v };

        [Test]
        public void FilterComboDeposits_NoActive_ReturnsAll()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Mars", MakeDepositGroup("water", MakeBadge(1000, 0.5f))),
            };
            var result = ResourceTrackerFilter.FilterComboDeposits(bodies, new HashSet<string>(), NoThresh(), NoThresh());
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void FilterComboDeposits_ActiveResourcePresent_Passes()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Mars", MakeDepositGroup("water", MakeBadge(1000, 0.5f))),
                MakeDepositBody("Moon", MakeDepositGroup("iron",  MakeBadge(500,  0.3f))),
            };
            var result = ResourceTrackerFilter.FilterComboDeposits(bodies, new HashSet<string> { "water" }, NoThresh(), NoThresh());
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Name, Is.EqualTo("Mars"));
        }

        [Test]
        public void FilterComboDeposits_ActiveResourceAbsent_Fails()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Mars", MakeDepositGroup("iron", MakeBadge(1000, 0.5f))),
            };
            var result = ResourceTrackerFilter.FilterComboDeposits(bodies, new HashSet<string> { "water" }, NoThresh(), NoThresh());
            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void FilterComboDeposits_PerResMinQtyNotMet_Fails()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Mars", MakeDepositGroup("water", MakeBadge(100, 0.5f))),
            };
            var result = ResourceTrackerFilter.FilterComboDeposits(
                bodies, new HashSet<string> { "water" }, Thresh("water", 200f), NoThresh());
            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void FilterComboDeposits_PerResMinQtyMet_Passes()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Mars", MakeDepositGroup("water", MakeBadge(500, 0.5f))),
            };
            var result = ResourceTrackerFilter.FilterComboDeposits(
                bodies, new HashSet<string> { "water" }, Thresh("water", 200f), NoThresh());
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void FilterComboDeposits_PerResMinQualityFiltersLowBadges_InsufficientQty()
        {
            // badge has factor 0.2, minQuality = 0.5 → badge excluded → qualifying qty = 0 < minQty=100
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Mars", MakeDepositGroup("water", MakeBadge(500, 0.2f))),
            };
            var result = ResourceTrackerFilter.FilterComboDeposits(
                bodies, new HashSet<string> { "water" }, Thresh("water", 100f), Thresh("water", 0.5f));
            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void FilterComboDeposits_TwoResources_BothMustPass()
        {
            // Mars has water (passes) + iron (fails qty), Moon has both passing
            var mars = MakeDepositBody("Mars",
                MakeDepositGroup("water", MakeBadge(500, 0.7f)),
                MakeDepositGroup("iron",  MakeBadge(50,  0.5f)));
            var moon = MakeDepositBody("Moon",
                MakeDepositGroup("water", MakeBadge(300, 0.7f)),
                MakeDepositGroup("iron",  MakeBadge(200, 0.5f)));
            var bodies = new List<BodyDepositGroup> { mars, moon };
            var perQty = new System.Collections.Generic.Dictionary<string, float>
                { ["water"] = 200f, ["iron"] = 100f };
            var result = ResourceTrackerFilter.FilterComboDeposits(
                bodies, new HashSet<string> { "water", "iron" }, perQty, NoThresh());
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Name, Is.EqualTo("Moon"));
        }

        [Test]
        public void FilterComboDeposits_NoActive_IgnoresThresholds_ReturnsAll()
        {
            // thresholds have no effect when no resources are selected
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Small", MakeDepositGroup("water", MakeBadge(10, 0.1f))),
                MakeDepositBody("Large", MakeDepositGroup("water", MakeBadge(500, 0.8f))),
            };
            var result = ResourceTrackerFilter.FilterComboDeposits(
                bodies, new HashSet<string>(), Thresh("water", 200f), NoThresh());
            Assert.That(result.Count, Is.EqualTo(2));
        }

        // ── FilterHiddenDeposits ──────────────────────────────────────────────

        [Test]
        public void FilterHiddenDeposits_EmptyHidden_ReturnsAll()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Mars", MakeDepositGroup("water", MakeBadge(500, 0.5f))),
            };
            var result = ResourceTrackerFilter.FilterHiddenDeposits(bodies, new HashSet<string>());
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void FilterHiddenDeposits_HiddenResourceRemoved()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Mars",
                    MakeDepositGroup("water", MakeBadge(500, 0.5f)),
                    MakeDepositGroup("iron",  MakeBadge(200, 0.3f))),
            };
            var result = ResourceTrackerFilter.FilterHiddenDeposits(bodies, new HashSet<string> { "water" });
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Groups.Count, Is.EqualTo(1));
            Assert.That(result[0].Groups[0].RdId, Is.EqualTo("iron"));
        }

        [Test]
        public void FilterHiddenDeposits_AllGroupsHidden_BodyRemoved()
        {
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Mars", MakeDepositGroup("water", MakeBadge(500, 0.5f))),
            };
            var result = ResourceTrackerFilter.FilterHiddenDeposits(bodies, new HashSet<string> { "water" });
            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void FilterHiddenDeposits_TotalEffRecalculated()
        {
            var grpWater = MakeDepositGroup("water", MakeBadge(500, 0.5f));  // eff = 250
            var grpIron  = MakeDepositGroup("iron",  MakeBadge(200, 0.4f));  // eff ≈ 80 (float factor)
            var bodies = new List<BodyDepositGroup>
            {
                MakeDepositBody("Mars", grpWater, grpIron),
            };
            var result = ResourceTrackerFilter.FilterHiddenDeposits(bodies, new HashSet<string> { "water" });
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].TotalEff, Is.EqualTo(80.0).Within(1e-4));
        }
    }
}
