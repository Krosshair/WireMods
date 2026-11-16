
using System;
using System.Collections.Generic;
using System.Reflection;
using GUIFramework;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace WiresDLSS
{
    // One native-looking dropdown in the existing graphics tab.
    // No extra configuration windows, keybinds, panels or animations.
    [HarmonyPatch]
    internal static class GraphicsSetting
    {
        private sealed class Entry
        {
            internal GuiDropdown dropdown;
            internal GameObject row;
        }

        private static readonly Dictionary<GraphicsSettings, Entry> Entries =
            new Dictionary<GraphicsSettings, Entry>();

        [HarmonyPatch(typeof(GraphicsSettings), "Initialize")]
        [HarmonyPostfix]
        private static void OnInitialize(GraphicsSettings __instance)
        {
            if (__instance == null || Entries.ContainsKey(__instance)) return;
            try
            {
                var field = AccessTools.Field(typeof(GraphicsSettings), "m_upscalingAlgorithmDropdown");
                var rootField = AccessTools.Field(typeof(GraphicsSettings), "m_listRoot");
                GuiDropdown original = field?.GetValue(__instance) as GuiDropdown;
                RectTransform list = rootField?.GetValue(__instance) as RectTransform;
                if (original == null || list == null)
                {
                    Plugin.Warn("Native graphics settings dropdown/root missing; no extra UI installed.");
                    return;
                }

                // Clone the nearest complete one-dropdown row. The outer
                // upscaling group may itself contain multiple dropdowns.
                Transform template = original.transform;
                while (template.parent != null && template.parent != list &&
                    template.parent.GetComponentsInChildren<GuiDropdown>(true).Length == 1)
                    template = template.parent;
                GameObject row = UnityEngine.Object.Instantiate(template.gameObject, list, false);
                row.name = "WiresDLSS_Mode";
                row.SetActive(true);
                row.transform.SetAsFirstSibling();
                var dropdowns = row.GetComponentsInChildren<GuiDropdown>(true);
                if (dropdowns.Length != 1)
                {
                    UnityEngine.Object.Destroy(row);
                    Plugin.Warn("DLSS settings template has " + dropdowns.Length + " dropdowns; UI injection aborted safely.");
                    return;
                }
                GuiDropdown dropdown = dropdowns[0];
                dropdown.onValueChanged.RemoveAllListeners();
                dropdown.ClearOptions();
                dropdown.AddOptions(new List<string> {
                    "Off", "DLAA", "DLSS Quality", "DLSS Balanced", "DLSS Performance"
                });
                dropdown.value = (int)Plugin.Current;
                dropdown.RefreshShownValue();
                foreach (TMP_Text label in row.GetComponentsInChildren<TMP_Text>(true))
                    if (label.name == "Label" || label.name == "Title")
                    {
                        label.text = "NVIDIA DLSS";
                        break;
                    }

                Entries[__instance] = new Entry { dropdown = dropdown, row = row };
                // Valheim sizes its settings list once during InitializeUI;
                // account for the extra row before Unity's layout rebuild.
                RectTransform rowRect = row.GetComponent<RectTransform>();
                if (rowRect != null)
                    list.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                        list.rect.height + Mathf.Max(40f, rowRect.rect.height) + 8f);
                LayoutRebuilder.MarkLayoutForRebuild(list);
                Plugin.Report("Added NVIDIA DLSS mode selector to native graphics settings.");
            }
            catch (Exception e)
            {
                Plugin.Warn("Failed to attach native DLSS setting: " + e.GetType().Name + ": " + e.Message);
            }
        }

        [HarmonyPatch(typeof(GraphicsSettings), "OnOkAsync")]
        [HarmonyPrefix]
        private static void OnSave(GraphicsSettings __instance)
        {
            if (__instance == null || !Entries.TryGetValue(__instance, out Entry entry) ||
                entry.dropdown == null) return;
            int desired = Mathf.Clamp(entry.dropdown.value, 0, 4);
            if (Plugin.ModeConfig.Value == desired) return;
            Plugin.ModeConfig.Value = desired;
            DLSSRenderer.InvalidateAll();
            Plugin.Report("NVIDIA DLSS mode changed: " + (Mode)desired);
        }

        [HarmonyPatch(typeof(GraphicsSettings), "Terminate")]
        [HarmonyPrefix]
        private static void OnClosed(GraphicsSettings __instance)
        {
            Entries.Remove(__instance);
        }
    }
}
