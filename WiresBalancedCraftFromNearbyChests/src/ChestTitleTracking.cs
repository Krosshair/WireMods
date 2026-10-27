using HarmonyLib;

namespace WiresBalancedCraftFromNearbyChests
{
    [HarmonyPatch(
        typeof(ZDO),
        nameof(ZDO.Set),
        new[] { typeof(int), typeof(string) })]
    internal static class ChestTitleZdoSetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ZDO __instance,
            int hash)
        {
            if (__instance == null ||
                hash != ZDOVars.s_text)
                return;

            Prof.Begin(Prof.ZdoText);
            try { ContainerRegistry.NotifyTitleChanged(__instance); }
            finally { Prof.End(Prof.ZdoText); }
        }
    }

    [HarmonyPatch(typeof(ZNetView), "OnDestroy")]
    internal static class RegistryObjectDestroyPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ZNetView __instance)
        {
            Prof.Begin(Prof.DestroyHook);
            try { PrefixTimed(__instance); }
            finally { Prof.End(Prof.DestroyHook); }
        }

        private static void PrefixTimed(ZNetView __instance)
        {
            if (__instance == null ||
                !ContainerRegistry.TrackedViews.Remove(__instance.GetInstanceID()))
                return;

            Container[] containers =
                __instance.GetComponentsInChildren<Container>(true);

            for (int i = 0; i < containers.Length; i++)
            {
                if (containers[i] != null)
                    ContainerRegistry.Unregister(containers[i]);
            }

            Smelter smelter =
                __instance.GetComponentInChildren<Smelter>(true);
            if (smelter != null)
                RefillTargetRegistry.Unregister(smelter);

            Fireplace fireplace =
                __instance.GetComponentInChildren<Fireplace>(true);
            if (fireplace != null)
                RefillTargetRegistry.Unregister(fireplace);

            CookingStation cooking =
                __instance.GetComponentInChildren<CookingStation>(true);
            if (cooking != null)
                RefillTargetRegistry.Unregister(cooking);

            Fermenter fermenter =
                __instance.GetComponentInChildren<Fermenter>(true);
            if (fermenter != null)
                RefillTargetRegistry.Unregister(fermenter);

            Turret turret =
                __instance.GetComponentInChildren<Turret>(true);
            if (turret != null)
                RefillTargetRegistry.Unregister(turret);

            ShieldGenerator shield =
                __instance.GetComponentInChildren<ShieldGenerator>(true);
            if (shield != null)
                RefillTargetRegistry.Unregister(shield);

            Catapult catapult =
                __instance.GetComponentInChildren<Catapult>(true);
            if (catapult != null)
                RefillTargetRegistry.Unregister(catapult);
        }
    }

    [HarmonyPatch(
        typeof(ZDO),
        nameof(ZDO.Set),
        new[] { typeof(string), typeof(string) })]
    internal static class ChestTitleZdoStringSetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ZDO __instance,
            string name)
        {
            if (__instance == null ||
                !string.Equals(
                    name,
                    "text",
                    System.StringComparison.Ordinal))
                return;

            ContainerRegistry.NotifyTitleChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(CraftingStation), "Start")]
    internal static class CraftingStationStartGraphPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            ConnectionVisuals.NotifyGraphChanged();
        }
    }

    [HarmonyPatch(typeof(CraftingStation), "OnDestroy")]
    internal static class CraftingStationDestroyGraphPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            ConnectionVisuals.NotifyGraphChanged();
        }
    }
}
