using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    // Menus fade and scale in when they open. Unity tells us when a panel is
    // enabled, so nothing watches for it.
    internal static class PanelMotionSystem
    {
        private const float Duration = 0.18f;

        internal static bool On =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.PanelAnimations.Value;

        // Skills, trophies and achievements: the panel just fades, and the rows
        // you can see fade and rise in one after another.
        private const float ListPanelFade = 0.1f;
        private const float RowStep = 0.025f;
        private const float RowDuration = 0.22f;
        private const int MaxRows = 14;
        private static readonly Vector3 RowFrom = new Vector3(0f, -8f, 0f);
        private static readonly Vector3[] Corners = new Vector3[4];

        internal static void Open(Transform root, float startScale = 0.95f, bool list = false)
        {
            if (!On || root == null)
                return;

            if (!list)
            {
                UiMotion.Enter(root, 0f, Duration, Vector3.zero, startScale, true);
                return;
            }

            UiMotion.Enter(root, 0f, ListPanelFade, Vector3.zero, 1f, true);
            // The game fills these lists right after showing the panel.
            UiMotion.NextFrame(() => Rows(root));
        }

        private static void Rows(Transform root)
        {
            if (root == null || !root.gameObject.activeInHierarchy)
                return;

            ScrollRect scroll = root.GetComponentInChildren<ScrollRect>();
            if (scroll == null || scroll.content == null)
                return;

            // Lay the list out first, so rows are measured where they'll stay.
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;

            // Some lists keep their rows in a single holder; look through it.
            Transform content = scroll.content;
            for (int depth = 0; depth < 2 && content.childCount == 1; depth++)
                content = content.GetChild(0);

            int index = 0;
            foreach (Transform row in content)
            {
                if (index >= MaxRows)
                    break;
                if (!row.gameObject.activeInHierarchy || !Visible(row as RectTransform, viewport))
                    continue;
                UiMotion.Enter(row, index * RowStep, RowDuration, RowFrom, 1f, true);
                index++;
            }
        }

        private static bool Visible(RectTransform row, RectTransform viewport)
        {
            if (row == null)
                return false;
            row.GetWorldCorners(Corners);
            float minY = Corners[0].y, maxY = Corners[1].y;
            viewport.GetWorldCorners(Corners);
            return maxY > Corners[0].y && minY < Corners[1].y;
        }

        internal static void Watch(Object target, bool list = false)
        {
            Transform t = target as Transform
                ?? (target as GameObject)?.transform
                ?? (target as Component)?.transform;
            if (t == null)
                return;
            PanelOpenWatcher watcher = t.GetComponent<PanelOpenWatcher>();
            if (watcher == null)
                watcher = t.gameObject.AddComponent<PanelOpenWatcher>();
            watcher.List |= list;
        }
    }

    internal sealed class PanelOpenWatcher : MonoBehaviour
    {
        internal bool List;

        private void OnEnable() => PanelMotionSystem.Open(transform, 0.95f, List);

        private void OnDisable() => UiMotion.Cancel(transform);
    }

    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class SmoothUIPanelInventoryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance)
        {
            PanelMotionSystem.Watch(__instance.m_skillsDialog, true);
            PanelMotionSystem.Watch(__instance.m_textsDialog);
            PanelMotionSystem.Watch(__instance.m_trophiesPanel, true);
            PanelMotionSystem.Watch(__instance.m_achievementsPanel, true);
            PanelMotionSystem.Watch(__instance.m_variantDialog);
            PanelMotionSystem.Watch(__instance.m_splitDialog);
            PanelMotionSystem.Watch(__instance.m_craftProgressPanel);
        }
    }

    [HarmonyPatch(typeof(TextInput), "Awake")]
    internal static class SmoothUIPanelTextInputPatch
    {
        [HarmonyPostfix]
        private static void Postfix(TextInput __instance) => PanelMotionSystem.Watch(__instance.m_panel);
    }

    [HarmonyPatch(typeof(Minimap), "Start")]
    internal static class SmoothUIPanelMapPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Minimap __instance)
        {
            PanelMotionSystem.Watch(__instance.m_largeRoot);
            PanelMotionSystem.Watch(__instance.m_nameInput);
        }
    }

    [HarmonyPatch(typeof(Menu), "Start")]
    internal static class SmoothUIPanelMenuPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Menu __instance)
        {
            PanelMotionSystem.Watch(__instance.m_menuDialog);
            PanelMotionSystem.Watch(__instance.m_quitDialog);
            PanelMotionSystem.Watch(__instance.m_logoutDialog);
        }
    }

    // Settings is created fresh each time it opens.
    [HarmonyPatch(typeof(Settings), "Awake")]
    internal static class SmoothUIPanelSettingsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Settings __instance) => PanelMotionSystem.Open(__instance.transform, 0.97f);
    }

    // Switching settings tabs fades the new page in.
    [HarmonyPatch(typeof(TabHandler), "SetActiveTab", new[] { typeof(int), typeof(bool), typeof(bool) })]
    internal static class SmoothUIPanelTabPatch
    {
        private static readonly FieldInfo SelectedField = AccessTools.Field(typeof(TabHandler), "m_selected");

        [HarmonyPrefix]
        private static void Prefix(TabHandler __instance, out int __state)
        {
            __state = SelectedField != null ? (int)SelectedField.GetValue(__instance) : -1;
        }

        [HarmonyPostfix]
        private static void Postfix(TabHandler __instance, int index, int __state)
        {
            if (index == __state || index < 0 || index >= __instance.m_tabs.Count)
                return;

            RectTransform page = __instance.m_tabs[index].m_page;
            if (page != null)
                PanelMotionSystem.Open(page, 0.98f);
        }
    }
}
