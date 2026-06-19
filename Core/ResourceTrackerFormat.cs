using System.Collections.Generic;

namespace SolarExpanseResourceTracker.Core
{
    public static class ResourceTrackerFormat
    {
        public static string FormatQty(double v)
        {
            if (v >= 1_000_000) return $"{v / 1_000_000:F0}MT";
            if (v >= 1_000)     return $"{v / 1_000:F0}KT";
            return $"{v:F0}T";
        }

        public static string FormatKT(double v)
        {
            if (v >= 1_000_000) return $"{v / 1_000_000:F1}MT";
            if (v >= 1_000)     return $"{v / 1_000:F1}KT";
            return $"{v:F1}T";
        }

        public static string FormatFlow(double v)
        {
            if (v >= 1_000) return $"{v / 1_000:F1}KT";
            return $"{v:F1}";
        }

        public static string FormatDays(double days)
        {
            if (days >= 1000 * 365) return "> 1000 y";
            if (days >= 365 * 2)    return $"{days / 365:F1}y";
            if (days >= 30)         return $"{days / 30:F1}mo";
            return $"{days:F1}d";
        }

        public static string FormatState(ResourceState s)
        {
            switch (s)
            {
                case ResourceState.Liquid:      return "Liq";
                case ResourceState.Gas:         return "Gas";
                case ResourceState.Underground: return "Und";
                default:                        return "Sol";
            }
        }

        public static string QualityColorHex(float f)
        {
            if (f >= 0.70f) return "#4CAF50";
            if (f >= 0.40f) return "#FFC107";
            return "#F44336";
        }

        public static string RowKey(string bodyName, string rdId) => bodyName + "\x1f" + rdId;
    }
}
