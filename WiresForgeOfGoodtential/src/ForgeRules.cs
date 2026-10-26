using UnityEngine;

namespace WiresForgeOfGoodtential
{
    internal static class ForgeRules
    {
        internal static float GetBaseWinChance(int potential)
        {
            return Mathf.Max(
                0.65f,
                0.90f - 0.05f * Mathf.Max(0, potential));
        }

        // Trophy bonuses to preserving an idol use the exact same
        // prestige comparison as trophy protection against failed rolls.
        // This is a relative 0..35% increase over the base 50% chance,
        // not +35 percentage points. With a very strong trophy the
        // actual preservation chance tops out at 67.5%.
        internal static float GetIdolKeepChance(
            ItemDrop.ItemData trophy,
            float itemReferencePrestige)
        {
            if (trophy == null ||
                ForgeTrophyEligibility.IsBossTrophy(trophy))
                return 0.50f;

            float badOutcomeMultiplier =
                ForgeData.GetTrophyBadOutcomeMultiplier(
                    trophy,
                    itemReferencePrestige);

            float favor =
                Mathf.Clamp01(
                    (1f - badOutcomeMultiplier) / 0.995f);

            return 0.50f * (1f + 0.35f * favor);
        }

        internal static bool IsTrophyDestructionProtection(
            ItemDrop.ItemData trophy,
            float itemReferencePrestige)
        {
            if (trophy == null ||
                ForgeTrophyEligibility.IsBossTrophy(trophy))
                return false;

            // Common, weak trophies cannot rescue an ultra-risky
            // gamble from genuine vanilla item destruction.
            // Useful trophies do, even when suffering fatigue.
            float prestige = ForgeData.GetTrophyPrestige(trophy);

            // Fatigue diminishes the favor percentage, but cannot
            // suddenly remove an already qualifying trophy's ability
            // to turn destruction into a reset.
            return prestige >= 13f &&
                Mathf.Exp(-0.17f *
                    (prestige - itemReferencePrestige + 14f))
                    <= 0.75f;
        }

        internal static bool LossRiskActive(int potential)
        {
            return potential >= 2;
        }

        internal static bool MultiLossRiskActive(int potential)
        {
            return potential >= 4;
        }

        internal static bool CompleteLossRiskActive(int potential)
        {
            // The +8 threshold also activates destruction for
            // extremely under-tier idol gambles.
            return potential >= 8;
        }

        internal static ForgeOdds GetOdds(
            int potential,
            int requiredIdolTier,
            int selectedIdolTier,
            ItemDrop.ItemData trophy,
            float itemReferencePrestige)
        {
            float baseSuccess =
                GetBaseWinChance(potential);

            float baseBad =
                1f - baseSuccess;

            int underTier =
                Mathf.Max(
                    0,
                    requiredIdolTier -
                    selectedIdolTier);

            if (underTier > 0)
            {
                // Under-tier idols cannot accept trophies. Ignore an
                // injected offering even when odds are queried outside
                // the UI / regular craft-selection path.
                trophy = null;

                // Under-tier idols reduce success and can trigger a
                // severe outcome at +8; 3+ tiers too weak can destroy.
                ForgeOdds risky = new ForgeOdds();

                risky.Success =
                    Mathf.Clamp01(
                        baseSuccess -
                        0.025f * underTier);

                float failure =
                    1f - risky.Success;

                // Most failed rolls reduce gained quality. A complete
                // reset only becomes possible at +8 beyond the natural
                // cap, then remains a small fraction of failures.
                float severe =
                    CompleteLossRiskActive(potential)
                        ? Mathf.Min(
                            0.07f,
                            failure * 0.10f +
                            (potential - 8) * 0.005f)
                        : 0f;

                // The under-tier branch always discards trophies,
                // so there is no separate trophy-protection path.
                bool ultraRisk =
                    underTier >= 3 &&
                    CompleteLossRiskActive(potential);

                risky.Destruction =
                    ultraRisk ? severe : 0f;
                risky.CompleteLoss =
                    severe - risky.Destruction;
                risky.OrdinaryFailure =
                    failure - severe;

                // At Qmax or Qmax+1 there isn't enough extra quality
                // for the ordinary loss rule to become meaningful.
                risky.FailureIsIgnore =
                    !LossRiskActive(potential);

                // Regular failures always remove exactly one quality.
                // The separate reset/destruction outcomes remain.
                risky.CascadeChance = 0f;

                risky.JackpotChance =
                    Mathf.Clamp01(
                        0.20f +
                        0.10f * underTier);

                return risky;
            }

            float baseComplete =
                CompleteLossRiskActive(potential)
                    ? Mathf.Min(
                        0.06f,
                        baseBad * 0.10f)
                    : 0f;

            float baseOrdinary =
                Mathf.Max(
                    0f,
                    baseBad - baseComplete);

            int overTier =
                Mathf.Max(
                    0,
                    selectedIdolTier -
                    requiredIdolTier);

            float idolOrdinaryMultiplier =
                Mathf.Pow(0.72f, overTier);

            float idolCompleteMultiplier =
                Mathf.Pow(0.45f, overTier);

            float trophyMultiplier =
                ForgeData.GetTrophyBadOutcomeMultiplier(
                    trophy,
                    itemReferencePrestige);

            float completeTrophyMultiplier =
                trophy == null
                    ? 1f
                    : Mathf.Pow(trophyMultiplier, 1.80f);

            float ordinary =
                baseOrdinary *
                idolOrdinaryMultiplier *
                trophyMultiplier;

            float complete =
                baseComplete *
                idolCompleteMultiplier *
                completeTrophyMultiplier;

            ordinary =
                Mathf.Clamp(
                    ordinary,
                    0f,
                    0.98f);

            complete =
                Mathf.Clamp(
                    complete,
                    0f,
                    0.98f - ordinary);

            ForgeOdds odds = new ForgeOdds();
            odds.OrdinaryFailure = ordinary;
            odds.CompleteLoss = complete;
            odds.Destruction = 0f;
            odds.JackpotChance = 0.20f;
            odds.Success =
                Mathf.Clamp01(
                    1f - ordinary - complete);
            odds.FailureIsIgnore =
                !LossRiskActive(potential);

            // No anti-jackpots: ordinary failed refinements no longer
            // cascade through several hard-earned quality levels.
            odds.CascadeChance = 0f;

            return odds;
        }

        internal static ForgeOutcome RollOutcome(
            ForgeOdds odds)
        {
            float roll =
                UnityEngine.Random.value;

            if (roll < odds.Success)
                return ForgeOutcome.Success;

            roll -= odds.Success;

            if (roll < odds.OrdinaryFailure)
            {
                return odds.FailureIsIgnore
                    ? ForgeOutcome.Ignore
                    : ForgeOutcome.Loss;
            }

            roll -= odds.OrdinaryFailure;

            if (roll < odds.CompleteLoss)
                return ForgeOutcome.CompleteLoss;

            if (odds.Destruction > 0f)
                return ForgeOutcome.Destroyed;

            // Guard against the tiny floating-point remainder on
            // non-destructive attempts: never invent destruction.
            return odds.FailureIsIgnore
                ? ForgeOutcome.Ignore
                : ForgeOutcome.Loss;
        }

        internal static int RollLossLevels(
            int potential,
            float cascadeChance)
        {
            // Deliberate single-level penalty for every ordinary fail,
            // regardless of the former cascade probability.
            return 1;
        }

        internal static int RollSuccessGain(
            float jackpotChance)
        {
            int gain = 1;

            if (UnityEngine.Random.value >=
                Mathf.Clamp01(jackpotChance))
            {
                return gain;
            }

            gain = 2;

            while (gain < 5 &&
                   UnityEngine.Random.value < 0.20f)
            {
                gain++;
            }

            return gain;
        }

        internal static string GetRiskWarning(
            ForgeOdds odds,
            int refinementLevel)
        {
            if (odds == null)
                return "";

            if (odds.Destruction > 0f)
                return "Refinement may completely destroy your item.";

            if (CompleteLossRiskActive(refinementLevel))
                return "Refinement may reduce your item back to its base form.";

            if (LossRiskActive(refinementLevel))
                return "Refinement may lower your item's quality.";

            return "Refinement may fail.";
        }
    }
}
