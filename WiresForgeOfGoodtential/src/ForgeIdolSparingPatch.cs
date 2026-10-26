using System;
using HarmonyLib;
using UnityEngine;

namespace WiresForgeOfGoodtential
{
    // The Forge has already validated the selected idol and optional trophy
    // before reaching Player.ConsumeResources. Every refinement outcome
    // (success, jackpot, ignore, loss, reset) can spare its selected idol.
    // A selected trophy improves the base 50% preservation chance using
    // its actual prestige relative to the refined item's progression.
    // Pass a separate array to vanilla; never change the recipe itself.
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class ForgeIdolSparingPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Player __instance,
            ref Piece.Requirement[] requirements)
        {
            if (!ForgeContext.Active ||
                __instance != ForgeContext.Player ||
                ForgeContext.Recipe == null ||
                !ReferenceEquals(
                    requirements,
                    ForgeContext.Recipe.m_resources) ||
                ForgeContext.UpgraderResource == null ||
                requirements == null)
            {
                return;
            }

            // Only the chosen idol is exempt. A trophy, if offered,
            // and all other requirements remain in the array.
            int idolIndex = -1;
            for (int i = 0; i < requirements.Length; i++)
            {
                Piece.Requirement requirement = requirements[i];
                if (requirement != null &&
                    requirement.m_upgraderResource &&
                    requirement.m_resItem != null &&
                    ReferenceEquals(
                        requirement.m_resItem.m_itemData,
                        ForgeContext.UpgraderResource))
                {
                    idolIndex = i;
                    break;
                }
            }

            if (idolIndex < 0 ||
                ForgeContext.ForceConsumeIdol ||
                UnityEngine.Random.value >=
                    ForgeContext.IdolKeepChance)
                return;

            Piece.Requirement[] remaining =
                new Piece.Requirement[requirements.Length - 1];

            if (idolIndex > 0)
                Array.Copy(requirements, 0, remaining, 0, idolIndex);

            int after = requirements.Length - idolIndex - 1;
            if (after > 0)
                Array.Copy(
                    requirements, idolIndex + 1,
                    remaining, idolIndex, after);

            requirements = remaining;
            ForgeContext.IdolSpared = true;
        }
    }
}
