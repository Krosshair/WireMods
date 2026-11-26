using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    // InventoryGui.HideRequirement deactivates a slot's icon, name and amount
    // but leaves its frame; the station level box keeps its frame when
    // m_minStationLevelIcon is off (no recipe, or no level needed).
    // Material slots: when nothing inside the slot is active, its frame Image
    // (also its tooltip's raycast target) is turned off. Anything a mod shows
    // in a slot (Forge of Goodtential's idol/trophy names, swap buttons)
    // keeps it as it is. Level box: hidden whole (CanvasGroup), since its
    // number sits inside it.
    // Everything runs on the children's OnEnable/OnDisable, a slot's
    // children changing, and the inventory opening.
    internal static class EmptyMaterialSlots
    {
        internal static bool On =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.HideEmptyMaterialSlots.Value;

        internal static void Refresh()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
                return;
            foreach (MaterialSlotWatcher w in gui.GetComponentsInChildren<MaterialSlotWatcher>(true))
                w.Apply();
            foreach (LevelIconWatcher w in gui.GetComponentsInChildren<LevelIconWatcher>(true))
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
                        Image frame = slot != null ? slot.GetComponent<Image>() : null;
                        if (frame == null || slot.GetComponent<MaterialSlotWatcher>() != null)
                            continue;
                        MaterialSlotWatcher w = slot.AddComponent<MaterialSlotWatcher>();
                        w.Frame = frame;
                        foreach (Transform child in slot.transform)
                            child.gameObject.AddComponent<MaterialPartWatcher>().Slot = w;
                        w.Apply();
                    }

                Image level = __instance.m_minStationLevelIcon;
                Transform box = level != null ? level.transform.parent : null;
                if (box != null && box.name == "level" && level.GetComponent<LevelIconWatcher>() == null)
                {
                    CanvasGroup group = box.GetComponent<CanvasGroup>();
                    if (group == null)
                        group = box.gameObject.AddComponent<CanvasGroup>();
                    LevelIconWatcher w = level.gameObject.AddComponent<LevelIconWatcher>();
                    w.Box = group;
                    w.Apply();
                }
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "Show")]
        private static class InventoryGuiShow
        {
            private static void Postfix() => Refresh();
        }
    }

    internal sealed class MaterialSlotWatcher : MonoBehaviour
    {
        internal Image Frame;
        // Only a frame this turned off is turned back on; one a mod turned off
        // is left to that mod.
        private bool _hidFrame;

        internal bool Empty
        {
            get
            {
                foreach (Transform child in transform)
                    if (child.gameObject.activeSelf)
                        return false;
                return true;
            }
        }

        internal void Apply()
        {
            if (Frame == null)
                return;
            bool show = !EmptyMaterialSlots.On || !Empty;
            if (!show && Frame.enabled)
            {
                Frame.enabled = false;
                _hidFrame = true;
            }
            else if (show && _hidFrame)
            {
                Frame.enabled = true;
                _hidFrame = false;
            }
        }

        private void OnTransformChildrenChanged() => Apply();
    }

    internal sealed class MaterialPartWatcher : MonoBehaviour
    {
        internal MaterialSlotWatcher Slot;

        private void OnEnable() { if (Slot != null) Slot.Apply(); }
        private void OnDisable() { if (Slot != null) Slot.Apply(); }
    }

    internal sealed class LevelIconWatcher : MonoBehaviour
    {
        internal CanvasGroup Box;

        internal void Apply()
        {
            if (Box == null)
                return;
            bool show = gameObject.activeSelf || !EmptyMaterialSlots.On;
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
