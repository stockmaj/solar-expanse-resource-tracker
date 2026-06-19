using System;
using System.Collections.Generic;

namespace SolarExpanseResourceTracker.Core
{
    public static class ResourceTrackerSort
    {
        public static void SortStockpiles(List<BodyStockpileGroup> bodies, string sortRes, string sortQual)
        {
            foreach (var b in bodies)
                b.Rows.Sort((a, x) => string.Compare(a.RdName, x.RdName, StringComparison.OrdinalIgnoreCase));

            if (sortRes == "" || sortRes == null)
            {
                bodies.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return;
            }

            bodies.Sort((a, b) =>
            {
                double va = StockSortValue(a, sortRes, sortQual);
                double vb = StockSortValue(b, sortRes, sortQual);
                bool aHas = va > double.MinValue / 2;
                bool bHas = vb > double.MinValue / 2;
                if (aHas != bHas) return aHas ? -1 : 1;
                int cmp = vb.CompareTo(va);
                if (cmp != 0) return cmp;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        public static double StockSortValue(BodyStockpileGroup body, string rdId, string qual)
        {
            var row = body.Rows.Find(r => r.RdId == rdId);
            if (row == null) return double.MinValue;
            switch (qual)
            {
                case "net":   return row.NetPerDay;
                case "lasts": return row.DaysLeft.HasValue ? row.DaysLeft.Value : double.MaxValue;
                default:      return row.Qty;
            }
        }

        public static void SortDeposits(List<BodyDepositGroup> bodies, string sortRes, string sortQual)
        {
            foreach (var b in bodies)
                b.Groups.Sort((a, x) => string.Compare(a.RdName, x.RdName, StringComparison.OrdinalIgnoreCase));

            if (sortRes == "" || sortRes == null)
            {
                bodies.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return;
            }

            bodies.Sort((a, b) =>
            {
                double va = DepositSortValue(a, sortRes, sortQual);
                double vb = DepositSortValue(b, sortRes, sortQual);
                bool aHas = va > double.MinValue / 2;
                bool bHas = vb > double.MinValue / 2;
                if (aHas != bHas) return aHas ? -1 : 1;
                int cmp = vb.CompareTo(va);
                if (cmp != 0) return cmp;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        public static double DepositSortValue(BodyDepositGroup body, string rdId, string qual)
        {
            var grp = body.Groups.Find(g => g.RdId == rdId);
            if (grp == null) return double.MinValue;
            switch (qual)
            {
                case "eff":         return grp.EffScore;
                case "bestquality": return grp.Badges.Count > 0 ? grp.Badges[0].Factor : 0;
                case "lasts":       return grp.TotalEstDays ?? double.MaxValue;
                default:            return grp.TotalSize;
            }
        }
    }
}
