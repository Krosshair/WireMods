using HarmonyLib;
using UnityEngine.UI;
using UnityEngine;

namespace WiresSmoothUI
{
    // InventoryGui.HideRequirement only deactivates a slot's icon, name and
    // amount, leaving the empty frame; the station level box keeps its frame
    // when m_minStationLevelIcon is off (no recipe, or no level needed).
    // A watcher on each icon hides its whole box (CanvasGroup alpha, so the
    // layout doesn't shift) while the icon is off. Runs on the icon's
    // OnEnable/OnDisable and when the inventory opens.
    // Upgrader stations (Forge of Goodtential) reuse these slots for their own
    // buttons, so nothing is hidden there.
    internal static class EmptyMaterialSlots
    {
        internal static bool On =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.HideEmptyMaterialSlots.Value;

        internal static bool AtUpgrader
        {
            get
            {
                Player player = Player.m_localPlayer;
                CraftingStation station = player != null ? player.GetCurrentCraftingStation() : null;
                return station != null && station.m_upgrader;
            }
        }

        internal static void Refresh()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
                return;
            foreach (MaterialIconWatcher w in gui.GetComponentsInChildren<MaterialIconWatcher>(true))
                w.Apply();
        }

        private static void Watch(GameObject icon, GameObject box)
        {
            if (icon == null || box == null || icon.GetComponent<MaterialIconWatcher>() != null)
                return;
            CanvasGroup group = box.GetComponent<CanvasGroup>();
            if (group == null)
                group = box.AddComponent<CanvasGroup>();
            MaterialIconWatcher w = icon.AddComponent<MaterialIconWatcher>();
            w.Box = group;
            w.Apply();
        }

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        private static class InventoryGuiAwake
        {
            private static void Postfix(InventoryGui __instance)
            {
                CraftingMotion.ResetShown();
                if (__instance.m_recipeRequirementList != null)
                    foreach (GameObject slot in __instance.m_recipeRequirementList)
                    {
                        Transform icon = slot != null ? slot.transform.Find("res_icon") : null;
                        if (icon != null)
                            Watch(icon.gameObject, slot);
                    }

                Image level = __instance.m_minStationLevelIcon;
                if (level != null && level.transform.parent != null && level.transform.parent.name == "level")
                    Watch(level.gameObject, level.transform.parent.gameObject);
            }
        }

        // Switching between a workbench and an upgrader means reopening the
        // inventory; slots whose icon didn't change need re-checking then.
        [HarmonyPatch(typeof(InventoryGui), "Show")]
        private static class InventoryGuiShow
        {
            private static void Postfix() => Refresh();
        }
    }

    internal sealed class MaterialIconWatcher : MonoBehaviour
    {
        internal CanvasGroup Box;

        internal void Apply()
        {
            if (Box == null)
                return;
            bool show = gameObject.activeSelf || !EmptyMaterialSlots.On || EmptyMaterialSlots.AtUpgrader;
            // A slide-in still running on the box would fade it back in.
            if (!show)
                UiMotion.Cancel(Box.transform);
            Box.alpha = show ? 1f : 0f;
            Box.blocksRaycasts = show;
        }

        private void OnEnable() => Apply();
        private void OnDisable() => Apply();
    }
}
