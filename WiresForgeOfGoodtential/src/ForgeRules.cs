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

        // ---- Keeping the idol ----
        //
        // The forge takes its due when it gives: a success keeps the idol
        // less often than a setback, and a reset almost always spares it.
        // On top of that:
        //  - each idol tier above the safe minimum adds 8 points, each
        //    tier below takes 8 away;
        //  - a trophy adds up to 25 points, by how well it suits the item
        //    (the same comparison that protects the item);
        //  - idol luck adds 4 points per idol lost in a row, up to 12.
        // With the safe idol and no trophy this averages about 50%.
        internal const float KeepAfterSuccess = 0.45f;
        internal const float KeepAfterSetback = 0.65f;
        internal const float KeepAfterReset = 0.85f;
        internal const float KeepAfterDestruction = 0.50f;
        private const float KeepPerTier = 0.08f;
        private const float KeepTrophyMax = 0.25f;
        private const float KeepMin = 0.15f;
        private const float KeepMax = 0.90f;

        internal static float GetIdolKeepChance(
            ForgeOutcome outcome,
            int idolTier,
            int requiredTier,
            ItemDrop.ItemData trophy,
            float itemReferencePrestige)
        {
            float chance;
            switch (outcome)
            {
                case ForgeOutcome.Success:
                case ForgeOutcome.Jackpot:
                    chance = KeepAfterSuccess;
                    break;
                case ForgeOutcome.CompleteLoss:
                    chance = KeepAfterReset;
                    break;
                case ForgeOutcome.Destroyed:
                    chance = KeepAfterDestruction;
                    break;
                default:
                    chance = KeepAfterSetback;
                    break;
            }

            return KeepChance(chance, idolTier, requiredTier, trophy, itemReferencePrestige);
        }

        // The same modifiers on any base, for showing "kept on success /
        // otherwise" before the roll.
        internal static float KeepChance(
            float baseChance,
            int idolTier,
            int requiredTier,
            ItemDrop.ItemData trophy,
            float itemReferencePrestige)
        {
            // Under-tier idols never take a trophy.
            if (idolTier < requiredTier)
                trophy = null;

            return Mathf.Clamp(
                baseChance +
                KeepPerTier * (idolTier - requiredTier) +
                TrophyKeepBonus(trophy, itemReferencePrestige) +
                IdolLuck.Bonus,
                KeepMin,
                KeepMax);
        }

        internal static float TrophyKeepBonus(
            ItemDrop.ItemData trophy,
            float itemReferencePrestige)
        {
            if (trophy == null ||
                ForgeTrophyEligibility.IsBossTrophy(trophy))
                return 0f;

            float multiplier =
                ForgeData.GetTrophyBadOutcomeMultiplier(
                    trophy,
                    itemReferencePrestige);

            return KeepTrophyMax *
                Mathf.Clamp01((1f - multiplier) / 0.995f);
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
