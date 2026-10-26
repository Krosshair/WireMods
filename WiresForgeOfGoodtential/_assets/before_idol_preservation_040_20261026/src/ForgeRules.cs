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
            // Complete reset is a late-stage risk, never item destruction.
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
                // An under-tier idol still risks more lost refinement,
                // but can NEVER delete the item or scrap its materials.
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
                risky.CompleteLoss =
                    CompleteLossRiskActive(potential)
                        ? Mathf.Min(
                            0.07f,
                            failure * 0.10f +
                            (potential - 8) * 0.005f)
                        : 0f;

                risky.OrdinaryFailure =
                    failure - risky.CompleteLoss;

                // At Qmax or Qmax+1 there isn't enough extra quality
                // for the ordinary loss rule to become meaningful.
                risky.FailureIsIgnore =
                    !LossRiskActive(potential);

                // Under-tier failures are more likely to cascade into
                // multi-level quality losses than safe idol failures.
                risky.CascadeChance =
                    Mathf.Clamp01(
                        baseBad *
                        (1f + 0.35f * underTier));

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
            odds.JackpotChance = 0.20f;
            odds.Success =
                Mathf.Clamp01(
                    1f - ordinary - complete);
            odds.FailureIsIgnore =
                !LossRiskActive(potential);

            float totalStabilizer =
                idolOrdinaryMultiplier *
                trophyMultiplier;

            odds.CascadeChance =
                Mathf.Clamp01(
                    baseBad *
                    Mathf.Sqrt(
                        Mathf.Clamp01(
                            totalStabilizer)));

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

            return ForgeOutcome.CompleteLoss;
        }

        internal static int RollLossLevels(
            int potential,
            float cascadeChance)
        {
            int available =
                Mathf.Max(1, potential);

            int lost = 1;

            if (!MultiLossRiskActive(potential))
                return lost;

            while (lost < available &&
                   UnityEngine.Random.value <
                       cascadeChance)
            {
                lost++;
            }

            return lost;
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

            if (CompleteLossRiskActive(refinementLevel))
                return "Refinement may reduce your item back to its base form.";

            if (MultiLossRiskActive(refinementLevel))
                return "Refinement may greatly lower your item's quality.";

            if (LossRiskActive(refinementLevel))
                return "Refinement may lower your item's quality.";

            return "Refinement may fail.";
        }
    }
}
