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

        internal static void WatchScaleOnly(Object target, float startScale)
        {
            Watch(target, startScale, true, true);
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
        }

        private void OnDisable() => UiMotion.Cancel(transform);
    }

    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class SmoothUIPanelInventoryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance)
        {
            // Skills, trophies and achievements open as they are, on purpose:
            // animating their long lists cost too much.
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
