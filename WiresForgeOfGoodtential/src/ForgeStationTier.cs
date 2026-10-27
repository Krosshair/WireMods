using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WiresForgeOfGoodtential
{
    // The station level box at the forge shows the selected item's tier
    // (ForgeSelection.ShownTier) instead of the recipe's required station
    // level, and never blinks red for it. UpdateRecipe writes the box every
    // frame from a local; the transpiler swaps that value and the station
    // level it's compared to, so the text only changes when the tier does.
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    internal static class ForgeStationTier
    {
        private static readonly MethodInfo RequiredLevel =
            AccessTools.Method(typeof(Recipe), nameof(Recipe.GetRequiredStationLevel));
        private static readonly MethodInfo StationLevel =
            AccessTools.Method(typeof(CraftingStation), nameof(CraftingStation.GetLevel));

        private static bool Active
        {
            get
            {
                if (ForgeSelection.CurrentRecipe == null || ForgeSelection.ShownTier <= 0)
                    return false;
                Player player = Player.m_localPlayer;
                CraftingStation station = player != null ? player.GetCurrentCraftingStation() : null;
                return station != null && station.m_upgrader;
            }
        }

        private static int Shown(int required) => Active ? ForgeSelection.ShownTier : required;

        private static int Level(CraftingStation station, bool checkExtensions) =>
            Active ? int.MaxValue : station.GetLevel(checkExtensions);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int call = code.FindIndex(c => c.Calls(RequiredLevel));
            if (call < 0)
            {
                UnityEngine.Debug.LogWarning("[Wire's Forge of Goodtential] " + "Station tier: GetRequiredStationLevel not found, the level box stays vanilla.");
                return code;
            }
            int store = code.FindIndex(call, c => c.IsStloc());
            int compare = store < 0 ? -1 : code.FindIndex(store, c => c.Calls(StationLevel));
            if (store < 0 || compare < 0)
            {
                UnityEngine.Debug.LogWarning("[Wire's Forge of Goodtential] " + "Station tier: level store/compare not found, the level box stays vanilla.");
                return code;
            }
            code[compare] = new CodeInstruction(System.Reflection.Emit.OpCodes.Call,
                AccessTools.Method(typeof(ForgeStationTier), nameof(Level))).MoveLabelsFrom(code[compare]);
            // The ternary's branches join on the store, so its labels move to the call.
            code.Insert(store, new CodeInstruction(System.Reflection.Emit.OpCodes.Call,
                AccessTools.Method(typeof(ForgeStationTier), nameof(Shown))).MoveLabelsFrom(code[store]));
            return code;
        }
    }
}
