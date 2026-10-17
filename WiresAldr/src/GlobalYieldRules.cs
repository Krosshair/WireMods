using System;
using System.Collections.Generic;

namespace WiresAldr
{
    internal enum GlobalResource
    {
        None, Wood, Stone, CoreWood, FineWood, Ashwood
    }

    internal sealed class GlobalYieldSettings
    {
        internal double EikthyrWood = 10;
        internal double ElderWood = 10;
        internal double LaterWood = 5;
        internal double ElderStone = 20;
        internal double LaterStone = 5;
        internal double ElderCoreWood = 20;
        internal double ElderFineWood = 10;
        internal double YagluthFineWood = 20;
        internal double FaderAshwood = 10;
    }

    // Distinct-boss material progression, independent of what biome
    // a resource happened to spawn in. All values are percentages.
    internal static class GlobalYieldRules
    {
        internal static List<BonusBreakdown.Portion> Parts(
            GlobalResource resource, bool[] killed, GlobalYieldSettings s)
        {
            var parts = new List<BonusBreakdown.Portion>();
            if (killed == null || killed.Length != 8 || s == null)
                return parts;

            switch (resource)
            {
                case GlobalResource.Wood:
                    Add(parts, killed, 0, s.EikthyrWood);
                    Add(parts, killed, 1, s.ElderWood);
                    for (int i = 2; i < 8; ++i)
                        Add(parts, killed, i, s.LaterWood);
                    break;
                case GlobalResource.Stone:
                    Add(parts, killed, 1, s.ElderStone);
                    for (int i = 2; i < 8; ++i)
                        Add(parts, killed, i, s.LaterStone);
                    break;
                case GlobalResource.CoreWood:
                    Add(parts, killed, 1, s.ElderCoreWood);
                    break;
                case GlobalResource.FineWood:
                    Add(parts, killed, 1, s.ElderFineWood);
                    Add(parts, killed, 4, s.YagluthFineWood);
                    break;
                case GlobalResource.Ashwood:
                    Add(parts, killed, 6, s.FaderAshwood);
                    break;
            }
            return parts;
        }

        internal static double Total(List<BonusBreakdown.Portion> parts)
        {
            double result = 0;
            foreach (BonusBreakdown.Portion part in parts)
                result += part.Percent;
            return result;
        }

        private static void Add(List<BonusBreakdown.Portion> parts,
            bool[] killed, int boss, double percent)
        {
            if (killed[boss] && percent > 0)
                parts.Add(new BonusBreakdown.Portion(boss, percent));
        }
    }
}
