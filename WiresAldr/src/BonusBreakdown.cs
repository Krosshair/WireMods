using System;
using System.Collections.Generic;

namespace WiresAldr
{
    // Presentation-only decomposition of the same distinct-boss
    // progression formula. No gameplay changes or new save keys.
    internal static class BonusBreakdown
    {
        internal struct Portion
        {
            internal int BossTier;
            internal double Percent;

            internal Portion(int bossTier, double percent)
            {
                BossTier = bossTier;
                Percent = percent;
            }
        }

        internal static List<Portion> ForBiome(
            int tier, bool[] defeated, double baseBonus,
            double laterFraction, double skippedFraction)
        {
            var parts = new List<Portion>();
            if (defeated == null || defeated.Length != ProgressionMath.BiomeCount ||
                tier < 0 || tier >= defeated.Length || baseBonus <= 0)
                return parts;

            laterFraction = Math.Max(0, Math.Min(1, laterFraction));
            skippedFraction = Math.Max(0, Math.Min(1, skippedFraction));
            bool ownDefeated = defeated[tier];
            for (int boss = tier; boss < defeated.Length; boss++)
            {
                if (!defeated[boss])
                    continue;

                double added = boss == tier
                    ? baseBonus
                    : baseBonus * Math.Pow(laterFraction, boss - tier)
                        * (ownDefeated ? 1 : skippedFraction);
                if (added > 0)
                    parts.Add(new Portion(boss, added));
            }
            return parts;
        }

        // Own-boss bonus plus a FLAT contribution for each boss defeated
        // after the biome's own boss (no distance decay).
        internal static List<Portion> Veteran(
            int tier, bool[] defeated, double ownBonus, double perLater,
            double skippedFraction, double laterFraction)
        {
            var parts = new List<Portion>();
            if (defeated == null || defeated.Length != ProgressionMath.BiomeCount ||
                tier < 0 || tier >= defeated.Length)
                return parts;
            bool own = defeated[tier];
            if (own && ownBonus > 0) parts.Add(new Portion(tier, ownBonus));
            double scale = own ? 1 : Math.Max(0, Math.Min(1, skippedFraction));
            // Tapers: each later Forsaken adds the later share of the previous one.
            double step = perLater * scale, taper = Math.Max(0, Math.Min(1, laterFraction));
            for (int boss = tier + 1; boss < defeated.Length; boss++)
                if (defeated[boss] && step > 0.0001)
                {
                    parts.Add(new Portion(boss, step));
                    step *= taper;
                }
            return parts;
        }

        // Pick the shortest readable precision for which every displayed
        // part really adds up to the displayed total.
        internal static int Precision(double total, List<Portion> parts)
        {
            for (int decimals = 2; decimals <= 8; decimals++)
            {
                double shownParts = 0;
                foreach (Portion part in parts)
                    shownParts += Math.Round(part.Percent, decimals,
                        MidpointRounding.AwayFromZero);
                double shownTotal = Math.Round(total, decimals,
                    MidpointRounding.AwayFromZero);
                if (Math.Abs(shownParts - shownTotal) < 0.00000001)
                    return decimals;
            }
            return 8;
        }
    }
}
