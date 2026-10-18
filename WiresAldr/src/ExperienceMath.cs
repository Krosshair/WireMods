using System;
namespace WiresAldr
{
    internal static class ExperienceMath
    {
        private static readonly double[] TierSteps = { 1, 1, 1.1, 1.25, 1.45, 1.7, 2.0, 2.35, 2.7, 3.0 };
        internal static double Progression(int tier, double strengthPercent)
        {
            tier = Math.Max(1, Math.Min(tier, TierSteps.Length - 1));
            return 1 + (TierSteps[tier] - 1) * Math.Max(0, strengthPercent) / 100.0;
        }
        // Biome exploration drives progression. Boss unlocks give a smaller
        // passive boost even in earlier biomes, never the full future-biome XP.
        internal static double BiomeAndProgress(int biomeTier, int defeatedTier, double strengthPercent, double progressShare)
        {
            biomeTier = Math.Max(1, Math.Min(biomeTier, TierSteps.Length - 1));
            defeatedTier = Math.Max(1, Math.Min(defeatedTier, TierSteps.Length - 1));
            double location = TierSteps[biomeTier] - 1;
            double history = TierSteps[defeatedTier] - 1;
            return 1 + (location + history * progressShare) * Math.Max(0, strengthPercent) / 100;
        }
        // The two parts of BiomeAndProgress, for display.
        internal static void BiomeParts(int biomeTier, int defeatedTier, double strengthPercent, double progressShare,
            out double location, out double history)
        {
            biomeTier = Math.Max(1, Math.Min(biomeTier, TierSteps.Length - 1));
            defeatedTier = Math.Max(1, Math.Min(defeatedTier, TierSteps.Length - 1));
            double k = Math.Max(0, strengthPercent) / 100;
            location = (TierSteps[biomeTier] - 1) * k;
            history = (TierSteps[defeatedTier] - 1) * progressShare * k;
        }
        internal static double CatchUpThreshold(int tier, double basis, double perTier) => basis + tier * perTier;
        internal static double CatchUp(int tier, double skillLevel, double strengthPercent, double basis, double perTier)
        {
            if (tier <= 1 || strengthPercent <= 0) return 1;
            double expected = CatchUpThreshold(tier, basis, perTier);
            if (expected <= 0) return 1;
            double gap = Math.Max(0, Math.Min(1, (expected - skillLevel) / expected));
            return 1 + gap * strengthPercent / 100.0;
        }
        internal static double Threat(int enemyTier, int playerTier, double strengthPercent)
        {
            int difference = enemyTier - playerTier;
            double danger = difference >= 0
                ? (difference == 0 ? 0.10 : Math.Min(0.18, 0.10 + difference * 0.04))
                : (difference == -1 ? 0.045 : 0.012);
            return Math.Max(0, strengthPercent / 100.0) * danger;
        }
        internal static double Parry(double staggerFraction, double strengthPercent)
        {
            if (staggerFraction < 0 || strengthPercent <= 0) return 0;
            double r = Math.Max(0, Math.Min(1, staggerFraction));
            return 0.05 * (1 + 9 * Math.Pow(r, 4)) * strengthPercent / 100.0;
        }
        internal static double Dodge(int confirmedStreak, double strengthPercent)
        {
            if (confirmedStreak <= 0 || strengthPercent <= 0) return 0;
            return Math.Min(0.3, 0.05 * confirmedStreak) * strengthPercent / 100.0;
        }
    }
}
