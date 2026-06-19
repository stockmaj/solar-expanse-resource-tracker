using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SolarExpanseResourceTracker.Core
{
    public static class ResourceTrackerFilter
    {
        public static List<BodyStockpileGroup> FilterCombo(
            List<BodyStockpileGroup> bodies, HashSet<string> activeResources)
        {
            if (activeResources == null || activeResources.Count == 0) return bodies;
            return bodies.Where(b =>
                activeResources.All(rid => b.Rows.Any(r => r.RdId == rid && r.Qty > 0))
            ).ToList();
        }

        public static List<BodyStockpileGroup> FilterHidden(
            List<BodyStockpileGroup> bodies, HashSet<string> hidden)
        {
            if (hidden == null || hidden.Count == 0) return bodies;
            var result = new List<BodyStockpileGroup>();
            foreach (var body in bodies)
            {
                var rows = body.Rows.Where(r => !hidden.Contains(r.RdId)).ToList();
                if (rows.Count == 0) continue;
                result.Add(new BodyStockpileGroup { Name = body.Name, Rows = rows });
            }
            return result;
        }

        public static List<BodyDepositGroup> FilterComboDeposits(
            List<BodyDepositGroup> bodies,
            HashSet<string> activeResources,
            IReadOnlyDictionary<string, float> minQtyPerRes,
            IReadOnlyDictionary<string, float> minQualPerRes)
        {
            if (activeResources == null || activeResources.Count == 0)
                return bodies;

            return bodies.Where(b =>
                activeResources.All(rid =>
                {
                    var grp = b.Groups.FirstOrDefault(g => g.RdId == rid);
                    if (grp == null) return false;

                    float minQty  = (minQtyPerRes  != null && minQtyPerRes.TryGetValue(rid,  out float q))  ? q  : 0f;
                    float minQual = (minQualPerRes != null && minQualPerRes.TryGetValue(rid, out float ql)) ? ql : 0f;
                    bool noQty  = minQty  <= 0;
                    bool noQual = minQual <= 0.01f;
                    if (noQty && noQual) return true;

                    double qualifying = grp.Badges
                        .Where(badge => noQual || badge.Factor >= minQual)
                        .Sum(badge => badge.Size);
                    return qualifying >= minQty;
                })
            ).ToList();
        }

        public static List<BodyDepositGroup> FilterHiddenDeposits(
            List<BodyDepositGroup> bodies, HashSet<string> hidden)
        {
            if (hidden == null || hidden.Count == 0) return bodies;
            var result = new List<BodyDepositGroup>();
            foreach (var body in bodies)
            {
                var groups = body.Groups.Where(g => !hidden.Contains(g.RdId)).ToList();
                if (groups.Count == 0) continue;
                result.Add(new BodyDepositGroup
                {
                    Name     = body.Name,
                    Groups   = groups,
                    TotalEff = groups.Sum(g => g.EffScore),
                });
            }
            return result;
        }
    }
}
