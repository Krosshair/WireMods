using System;
namespace WiresAldr
{
    // Each distinct boss contributes only once. Later bosses decay by distance.
    internal static class ProgressionMath
    {
        internal const int BiomeCount = 8;

        internal static double Bonus(int tier, bool[] defeated,
            double ownBonus, double laterFraction, double skippedFraction)
        {
            if (tier < 0 || tier >= BiomeCount || defeated == null ||
                defeated.Length != BiomeCount || ownBonus <= 0) return 0;
            laterFraction = Clamp(laterFraction, 0, 1);
            skippedFraction = Clamp(skippedFraction, 0, 1);
            bool own = defeated[tier];
            double total = own ? ownBonus : 0;
            for (int i = tier + 1; i < BiomeCount; ++i)
                if (defeated[i])
                    total += ownBonus * Math.Pow(laterFraction, i - tier)
                        * (own ? 1 : skippedFraction);
            return total;
        }

        internal static double HarvestChance(double nativeChance, double extraPP, double capPP)
        {
            nativeChance = Clamp(nativeChance, 0, 1);
            return Math.Min(Clamp(capPP / 100.0, 0, 1),
                nativeChance + Math.Max(0, extraPP / 100.0));
        }

        internal static int ExtraCount(int baseCount, double percent, double random)
        {
            if (baseCount <= 0 || percent <= 0) return 0;
            double amount = baseCount * Math.Max(0, percent) / 100.0;
            int full = (int)Math.Floor(amount);
            return full + (random < amount - full ? 1 : 0);
        }

        internal static double StarChance(double vanillaChance, double vanillaBase, double extraPP, double cap = 95)
        {
            // Difficulty modifiers have already affected vanillaChance.
            if (vanillaChance <= 0 || vanillaBase <= 0 || extraPP <= 0)
                return vanillaChance;
            return Clamp(vanillaChance +
                extraPP * (vanillaChance / vanillaBase), 0, Math.Max(vanillaChance, cap));
        }

        private static double Clamp(double v, double lo, double hi)
            => Math.Max(lo, Math.Min(hi, v));
    }
}
