using System.Collections.Generic;

namespace SolarExpanseResourceTracker.Core
{
    // Mirrors game's RowResourcesData.EResourceState (int values must match exactly)
    public enum ResourceState { Solid = 0, Liquid = 1, Gas = 2, Underground = 4 }

    public sealed class StockpileRow
    {
        public string RdId;
        public string RdName;
        public double Qty;
        public ResourceState State;
        public double InPerDay, OutPerDay, NetPerDay;
        public double? DaysLeft;
    }

    public sealed class BodyStockpileGroup
    {
        public string Name;
        public System.Collections.Generic.List<StockpileRow> Rows = new System.Collections.Generic.List<StockpileRow>();
    }

    public sealed class DepositBadge
    {
        public double Size;
        public float Factor;
        public ResourceState State;
        public double OutTakePerDay;
        public double? EstDays;
        public bool Preliminary;
    }

    public sealed class DepositGroup
    {
        public string RdId;
        public string RdName;
        public double TotalSize;
        public double EffScore;
        public double RatePerUnit;
        public double? TotalEstDays;
        public bool Preliminary;
        public System.Collections.Generic.List<DepositBadge> Badges = new System.Collections.Generic.List<DepositBadge>();
    }

    public sealed class BodyDepositGroup
    {
        public string Name;
        public System.Collections.Generic.List<DepositGroup> Groups = new System.Collections.Generic.List<DepositGroup>();
        public double TotalEff;
    }
}
