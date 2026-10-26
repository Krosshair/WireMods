using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WiresSmoothUI
{
    // Menus coming into view.
    //
    // A plain dialog fades and scales in. A menu with lists gets only a quick
    // fade, and the rows you can actually see slide in one after another, so
    // the list arrives instead of the whole panel growing. Rows scrolled out of
    // view aren't touched, which keeps long lists (trophies, settings) cheap.
    // Unity tells us when a panel is enabled, so nothing watches for it.
    internal static class PanelMotionSystem
    {
        private const float DialogDuration = 0.18f;
        private const float DialogStartScale = 0.95f;
        private const float ListPanelFade = 0.1f;

        private const float RowStart = 0.03f;
        private const float RowStep = 0.03f;
        private const float RowDuration = 0.22f;
        private const int MaxRows = 14;
        private static readonly Vector3 RowFrom = new Vector3(0f, -8f, 0f);

        private static readonly Vector3[] Corners = new Vector3[4];

        internal static bool On =>
            WiresSmoothUIPlugin.Enabled.Value && WiresSmoothUIPlugin.PanelAnimations.Value;

        internal static void Open(Transform root, bool buttons = false, float startScale = DialogStartScale)
        {
            if (!On || root == null || !root.gameObject.activeInHierarchy)
                return;

            bool lists = buttons || root.GetComponentInChildren<ScrollRect>() != null;
            if (!lists)
            {
                UiMotion.Enter(root, 0f, DialogDuration, Vector3.zero, startScale, true);
                return;
            }

            UiMotion.Enter(root, 0f, ListPanelFade, Vector3.zero, 1f, true);

            // Lists are usually filled right after the panel turns on, so the
            // rows go in on the next frame, once they exist and are laid out.
            UiMotion.NextFrame(() => Contents(root, buttons));
        }

        private static void Contents(Transform root, bool buttons)
        {
            if (root == null || !root.gameObject.activeInHierarchy)
                return;

            // Settle layout first, so rows are measured where they'll stay.
            Canvas.ForceUpdateCanvases();

            foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>())
                if (scroll.content != null)
                    Rows(RowsOf(scroll.content), Viewport(scroll));

            if (buttons)
                Rows(root.GetComponentsInChildren<Button>(), null);
        }

        // Slides rows in one after another. With a viewport, only rows inside it.
        internal static void Rows(IEnumerable<Component> rows, RectTransform viewport)
        {
            int index = 0;
            foreach (Component row in rows)
            {
                if (index >= MaxRows)
                    break;
                if (row == null || !row.gameObject.activeInHierarchy)
                    continue;
                if (viewport != null && !Overlaps(row.transform as RectTransform, viewport))
                    continue;

                UiMotion.Enter(row.transform, RowStart + index * RowStep, RowDuration, RowFrom, 1f, true);
                index++;
            }
        }

        // A list's rows are the active children of its content. Some lists
        // wrap their rows in a single holder; look through those.
        private static List<Component> RowsOf(Transform content)
        {
            List<Component> rows = new List<Component>();
            for (int depth = 0; depth < 3 && content != null; depth++)
            {
                rows.Clear();
                foreach (Transform child in content)
                    if (child.gameObject.activeInHierarchy)
                        rows.Add(child);
                if (rows.Count != 1)
                    break;
                content = (Transform)rows[0];
            }
            return rows;
        }

        internal static RectTransform Viewport(ScrollRect scroll) =>
            scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;

        private static bool Overlaps(RectTransform row, RectTransform viewport)
        {
            if (row == null)
                return false;
            row.GetWorldCorners(Corners);
            float rowMinY = Corners[0].y, rowMaxY = Corners[1].y, rowMinX = Corners[0].x, rowMaxX = Corners[2].x;
            viewport.GetWorldCorners(Corners);
            return rowMaxY > Corners[0].y && rowMinY < Corners[1].y
                && rowMaxX > Corners[0].x && rowMinX < Corners[2].x;
        }

        internal static void Watch(Object target, bool buttons = false)
        {
            Transform t = target as Transform
                ?? (target as GameObject)?.transform
                ?? (target as Component)?.transform;
            if (t == null)
                return;

            PanelOpenWatcher watcher = t.GetComponent<PanelOpenWatcher>();
            if (watcher == null)
                watcher = t.gameObject.AddComponent<PanelOpenWatcher>();
            watcher.Buttons |= buttons;
        }
    }

    internal sealed class PanelOpenWatcher : MonoBehaviour
    {
        internal bool Buttons;

        private void OnEnable() => PanelMotionSystem.Open(transform, Buttons);

        private void OnDisable() => UiMotion.Cancel(transform);
    }

    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class SmoothUIPanelInventoryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance)
        {
            PanelMotionSystem.Watch(__instance.m_skillsDialog);
            PanelMotionSystem.Watch(__instance.m_textsDialog);
            PanelMotionSystem.Watch(__instance.m_trophiesPanel);
            PanelMotionSystem.Watch(__instance.m_achievementsPanel);
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
            PanelMotionSystem.Watch(__instance.m_menuDialog, true);
            PanelMotionSystem.Watch(__instance.m_quitDialog);
            PanelMotionSystem.Watch(__instance.m_logoutDialog);
        }
    }

    // Settings is created fresh each time it opens.
    [HarmonyPatch(typeof(Settings), "Awake")]
    internal static class SmoothUIPanelSettingsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Settings __instance) => PanelMotionSystem.Open(__instance.transform, false, 0.97f);
    }

    // Switching tabs brings the new page in and pops the tab's label.
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

            TabHandler.Tab tab = __instance.m_tabs[index];
            if (tab.m_page != null)
                PanelMotionSystem.Open(tab.m_page, false, 0.98f);
            FeedbackPops.Tab(tab.m_button);
        }
    }

    [HarmonyPatch(typeof(FejdStartup), "Awake")]
    internal static class SmoothUIPanelMainMenuPatch
    {
        private static readonly string[] Panels =
        {
            "m_characterSelectScreen", "m_newCharacterPanel", "m_creditsPanel",
            "m_startGamePanel", "m_createWorldPanel", "m_removeWorldDialog",
            "m_removeCharacterDialog", "m_connectionFailedPanel"
        };

        [HarmonyPostfix]
        private static void Postfix(FejdStartup __instance)
        {
            Traverse t = Traverse.Create(__instance);
            PanelMotionSystem.Watch(t.Field("m_mainMenu").GetValue() as GameObject, true);
            foreach (string name in Panels)
                PanelMotionSystem.Watch(t.Field(name).GetValue() as GameObject);
        }
    }
}
