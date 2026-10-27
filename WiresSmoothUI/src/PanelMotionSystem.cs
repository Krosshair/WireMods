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

        internal static void Open(Transform root, float startScale = 0.95f)
        {
            if (!On || root == null)
                return;
            UiMotion.Enter(root, 0f, Duration, Vector3.zero, startScale, true);
        }

        // Its own canvas: moving, scaling or fading it then only redraws
        // it, not everything else on the HUD with it.
        internal static bool OwnCanvas(GameObject go)
        {
            try
            {
                if (go.GetComponent<Canvas>() != null)
                    return true;
                Canvas parent = go.GetComponentInParent<Canvas>();
                if (parent == null)
                    return false;
                GraphicRaycaster parentCaster = parent.GetComponent<GraphicRaycaster>();
                Canvas canvas = go.AddComponent<Canvas>();
                canvas.overrideSorting = false;
                canvas.additionalShaderChannels = parent.additionalShaderChannels;
                // Clicks on its buttons need a raycaster on the new canvas.
                GraphicRaycaster caster = go.AddComponent<GraphicRaycaster>();
                if (parentCaster != null)
                {
                    caster.ignoreReversedGraphics = parentCaster.ignoreReversedGraphics;
                    caster.blockingObjects = parentCaster.blockingObjects;
                }
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Wire's Smooth UI] Own canvas for " + go.name + ": " + e.Message);
                return false;
            }
        }

        // Eases up from startScale with no fade.
        internal static void Pop(Transform root, float startScale)
        {
            if (!On || root == null)
                return;
            UiMotion.Enter(root, 0f, Duration, Vector3.zero, startScale, false);
        }

        // These panels have a full-screen darkening backdrop as a child; pop
        // the window itself (largest child that isn't stretched over the whole
        // parent) and leave the backdrop alone. Falls back to the panel.
        internal static Transform Frame(Object target)
        {
            Transform root = target as Transform
                ?? (target as GameObject)?.transform
                ?? (target as Component)?.transform;
            if (root == null)
                return null;
            Transform best = null;
            float bestArea = 0f;
            foreach (Transform child in root)
            {
                RectTransform rt = child as RectTransform;
                if (rt == null)
                    continue;
                bool fullScreen = rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one &&
                                  rt.sizeDelta.x >= -1f && rt.sizeDelta.y >= -1f;
                float area = rt.rect.width * rt.rect.height;
                if (!fullScreen && area > bestArea)
                {
                    best = child;
                    bestArea = area;
                }
            }
            return best != null ? best : root;
        }

        internal static void WatchScaleOnly(Object target, float startScale)
        {
            Watch(target, startScale, true, true);
        }

        // Rows of a list inside the panel fade and rise in one after another,
        // every row you can see (the list is laid out and scrolled first).
        private const float RowStep = 0.025f;
        private const float MaxRowDelay = 0.3f;
        private const float RowDuration = 0.22f;
        private static readonly Vector3 RowFrom = new Vector3(0f, -8f, 0f);
        private static readonly Vector3[] Corners = new Vector3[4];

        internal static void WatchRows(Object target)
        {
            Transform t = target as Transform
                ?? (target as GameObject)?.transform
                ?? (target as Component)?.transform;
            PanelOpenWatcher watcher = t != null ? t.GetComponent<PanelOpenWatcher>() : null;
            if (watcher != null)
                watcher.Rows = true;
        }

        internal static void Rows(Transform root)
        {
            if (!On || root == null || !root.gameObject.activeInHierarchy)
                return;
            ScrollRect scroll = root.GetComponentInChildren<ScrollRect>();
            if (scroll == null && root.parent != null)
                scroll = root.parent.GetComponentInChildren<ScrollRect>();
            if (scroll == null || scroll.content == null)
                return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;

            // Some lists keep their rows in a single holder.
            Transform content = scroll.content;
            for (int depth = 0; depth < 2 && content.childCount == 1; depth++)
                content = content.GetChild(0);

            int index = 0;
            foreach (Transform row in content)
            {
                if (!row.gameObject.activeInHierarchy || !Visible(row as RectTransform, viewport))
                    continue;
                UiMotion.Enter(row, Mathf.Min(MaxRowDelay, index * RowStep), RowDuration, RowFrom, 1f, true);
                index++;
            }
        }

        private static bool Visible(RectTransform row, RectTransform viewport)
        {
            if (row == null || viewport == null)
                return false;
            row.GetWorldCorners(Corners);
            float minY = Corners[0].y, maxY = Corners[1].y;
            viewport.GetWorldCorners(Corners);
            return maxY > Corners[0].y && minY < Corners[1].y;
        }

        // ownCanvas: the panel gets a canvas of its own the first time it
        // animates (only with Panels on), so its animation redraws only it.
        internal static void Watch(Object target, float startScale = 0.95f, bool scaleOnly = false,
            bool ownCanvas = false)
        {
            Transform t = target as Transform
                ?? (target as GameObject)?.transform
                ?? (target as Component)?.transform;
            if (t == null)
                return;
            PanelOpenWatcher watcher = t.GetComponent<PanelOpenWatcher>();
            if (watcher == null)
                watcher = t.gameObject.AddComponent<PanelOpenWatcher>();
            watcher.StartScale = startScale;
            watcher.ScaleOnly = scaleOnly;
            watcher.OwnCanvas = ownCanvas;
            // Set up: from the next time the panel opens, it animates.
            // (Adding it to a panel that's open right now doesn't.)
            watcher.Ready = true;
        }
    }

    internal sealed class PanelOpenWatcher : MonoBehaviour
    {
        internal float StartScale = 0.95f;
        internal bool ScaleOnly;
        internal bool OwnCanvas;
        internal bool Rows;
        internal bool Ready;
        private int _canvas; // 0 not tried, 1 has one, -1 couldn't: don't animate

        private void OnEnable()
        {
            if (!Ready || !PanelMotionSystem.On)
                return;
            if (OwnCanvas)
            {
                if (_canvas == 0)
                    _canvas = PanelMotionSystem.OwnCanvas(gameObject) ? 1 : -1;
                if (_canvas < 0)
                    return; // no cheap way to animate it: leave it as it is
            }
            if (ScaleOnly) PanelMotionSystem.Pop(transform, StartScale);
            else PanelMotionSystem.Open(transform, StartScale);
            if (Rows)
            {
                // The game fills and scrolls the list right after showing it.
                Transform root = transform;
                UiMotion.NextFrame(() => PanelMotionSystem.Rows(root));
            }
        }

        private void OnDisable() => UiMotion.Cancel(transform);
    }

    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class SmoothUIPanelInventoryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance)
        {
            // Skills, trophies and achievements hold long lists: a scale-only
            // pop on their own canvas, like the build menu, so the lists
            // aren't redrawn every frame of it.
            PanelMotionSystem.WatchScaleOnly(PanelMotionSystem.Frame(__instance.m_skillsDialog), 0.93f);
            PanelMotionSystem.WatchRows(PanelMotionSystem.Frame(__instance.m_skillsDialog));
            PanelMotionSystem.WatchScaleOnly(PanelMotionSystem.Frame(__instance.m_trophiesPanel), 0.93f);
            PanelMotionSystem.WatchScaleOnly(PanelMotionSystem.Frame(__instance.m_achievementsPanel), 0.93f);
            PanelMotionSystem.Watch(__instance.m_textsDialog);
            PanelMotionSystem.Watch(__instance.m_variantDialog);
            PanelMotionSystem.Watch(__instance.m_splitDialog);
            PanelMotionSystem.Watch(__instance.m_craftProgressPanel);
        }
    }

    // The hammer's build menu pops in like other menus. It holds every piece
    // you can build, often hundreds of buttons, and sits on the HUD's canvas:
    // scaling or fading it there would rebuild the whole HUD every frame of
    // the pop. On a canvas of its own, scaling it only moves that canvas, so
    // the pop is a scale only, with no fade.
    [HarmonyPatch(typeof(BuildUi), "Awake")]
    internal static class SmoothUIPanelBuildMenuPatch
    {
        [HarmonyPostfix]
        private static void Postfix(BuildUi __instance)
        {
            if (__instance != null)
                PanelMotionSystem.WatchScaleOnly(__instance, 0.93f);
        }

    }

    // The selected piece panel at the bottom is part of the HUD: it fades in
    // when you take out the hammer.
    [HarmonyPatch(typeof(Hud), "Awake")]
    internal static class SmoothUIPanelBuildHudPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Hud __instance)
        {
            if (__instance.m_buildHud != null)
                PanelMotionSystem.Watch(__instance.m_buildHud, 1f, false, true);
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
