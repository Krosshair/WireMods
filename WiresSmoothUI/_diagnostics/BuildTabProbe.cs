using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace WiresSmoothUI
{
    // Temporary diagnostic: what changes size in the build menu's category list when switching tabs.
    // Logs the list's container, its scroll content and the first tag buttons (scale, size, font)
    // after each tab switch, for the first 12 switches of a session.
    [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.SelectPieceList))]
    internal static class BuildTabProbe
    {
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("SmoothUI probe");
        private static readonly FieldInfo Buttons = AccessTools.Field(typeof(BuildUi), "m_tagButtons");
        private static readonly FieldInfo Content = AccessTools.Field(typeof(BuildUi), "m_tagListScrollContent");
        private static readonly FieldInfo Container = AccessTools.Field(typeof(BuildUi), "m_tagListContainer");
        private static readonly FieldInfo ShowAll = AccessTools.Field(typeof(BuildUi), "m_showAllTagsButton");
        private static int s_count;

        private static void Postfix(BuildUi __instance, int tabIndex)
        {
            if (s_count++ >= 12) return;
            var sb = new System.Text.StringBuilder("tab " + tabIndex + ":");
            Describe(sb, "container", Container?.GetValue(__instance) as RectTransform);
            Describe(sb, "content", Content?.GetValue(__instance) as RectTransform);
            Describe(sb, "showAll", (ShowAll?.GetValue(__instance) as Component)?.transform as RectTransform);
            if (Buttons?.GetValue(__instance) is IList<BuildUiTagButton> list)
                for (int i = 0; i < list.Count && i < 3; i++)
                    Describe(sb, "tag" + i, list[i] != null ? list[i].transform as RectTransform : null);
            Log.LogInfo(sb.ToString());
        }

        private static void Describe(System.Text.StringBuilder sb, string name, RectTransform t)
        {
            if (t == null) { sb.Append(" | " + name + " none"); return; }
            TMP_Text text = t.GetComponentInChildren<TMP_Text>(true);
            sb.Append(" | " + name + " '" + t.name + "' scale " + t.localScale.ToString("F3") + " lossy " + t.lossyScale.ToString("F4") +
                " size " + t.rect.size.ToString("F1") + (text != null ? " font " + text.fontSize.ToString("F1") + (text.enableAutoSizing ? " auto" : "") : ""));
        }
    }
}
