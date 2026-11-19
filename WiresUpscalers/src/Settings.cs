using System;
using System.Collections.Generic;
using GUIFramework;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace WiresUpscalers
{
    // The game's own graphics settings: the upscalers join Smooth/Pixelated in its upscaling row (the
    // game keeps its stretch setting underneath, Smooth for ours), and a DLSS model or FSR sharpness
    // row appears under it for those. Saved with the game's OK, dropped with its cancel.
    [HarmonyPatch]
    internal static class Settings
    {
        private static readonly Upscaler[] Order = { Upscaler.DLSS, Upscaler.FSR3, Upscaler.FSR4, Upscaler.XeSS };
        private static readonly string[] Names = { "NVIDIA® DLSS", "AMD FSR™ 3", "AMD FSR™ 4", "Intel® XeSS" };
        private static readonly int[] Models = { 13, 11, 5 };
        private static readonly string[] ModelNames = { "Transformer 2 (DLSS 4.5)", "Transformer (DLSS 4)", "CNN (DLSS 3)" };
        private static readonly float[] Sharp = { 0f, .25f, .5f, .75f, 1f };
        private static readonly string[] SharpNames = { "Off", "25%", "50%", "75%", "100%" };

        private sealed class Ui
        {
            internal GuiDropdown upscaling, model, sharpness;
            internal GameObject modelRow, sharpRow;
            internal int gameOptions;                       // the game's own entries (Smooth, Pixelated)
            internal readonly List<Upscaler> listed = new List<Upscaler>(4);
            internal RectTransform list;
        }
        private static readonly Dictionary<GraphicsSettings, Ui> Open = new Dictionary<GraphicsSettings, Ui>();

        private static readonly AccessTools.FieldRef<GraphicsSettings, GuiDropdown> UpscalingField =
            AccessTools.FieldRefAccess<GraphicsSettings, GuiDropdown>("m_upscalingAlgorithmDropdown");
        private static readonly AccessTools.FieldRef<GraphicsSettings, List<QualityDropdownData>> DropdownsField =
            AccessTools.FieldRefAccess<GraphicsSettings, List<QualityDropdownData>>("m_qualityDropdowns");
        private static readonly AccessTools.FieldRef<GraphicsSettings, RectTransform> ListField =
            AccessTools.FieldRefAccess<GraphicsSettings, RectTransform>("m_listRoot");

        private static Upscaler Selected(Ui ui)
        {
            int i = ui.upscaling.value - ui.gameOptions;
            return i >= 0 && i < ui.listed.Count ? ui.listed[i] : Upscaler.None;
        }

        [HarmonyPatch(typeof(GraphicsSettings), "InitializeUpscalingAlgorithmsDropdown")]
        [HarmonyPostfix]
        private static void Build(GraphicsSettings __instance)
        {
            try
            {
                GuiDropdown d = UpscalingField(__instance);
                List<QualityDropdownData> all = DropdownsField(__instance);
                RectTransform list = ListField(__instance);
                if (d == null || all == null || all.Count == 0 || list == null) return;
                var ui = new Ui { upscaling = d, gameOptions = d.options.Count, list = list };
                bool every = Plugin.ShowAll.Value;
                var names = new List<string>();
                for (int i = 0; i < Order.Length; i++)
                    if (every || Native.Has(Order[i])) { ui.listed.Add(Order[i]); names.Add(Names[i]); }
                d.AddOptions(names);
                // the game's setting under ours stays Smooth (0)
                var values = new List<int>();
                for (int i = 0; i < ui.gameOptions; i++) values.Add(i);
                for (int i = 0; i < ui.listed.Count; i++) values.Add(0);
                all[all.Count - 1] = new QualityDropdownData(GraphicsSettingInt.UpscalingAlgorithm, d, null, values);

                ui.modelRow = Row(d, list, "DLSS model", ModelNames, out ui.model);
                ui.sharpRow = Row(d, list, "FSR sharpness", SharpNames, out ui.sharpness);
                d.onValueChanged.AddListener(_ => Refresh(ui));
                Open[__instance] = ui;
            }
            catch (Exception e) { Plugin.Warn("Upscaler settings not added: " + e.Message); }
        }

        // A row of its own in the settings list, under the game's resolution and upscaling group: a copy
        // of the upscaling dropdown with its label ("Scaling mode" in the game's prefab, which sits beside
        // the render-scale dropdown inside that group, so it can't go in there).
        private static GameObject Row(GuiDropdown template, RectTransform list, string title, string[] options, out GuiDropdown dropdown)
        {
            Transform labelled = template.transform, item = template.transform;
            while (labelled.parent != null && labelled.parent != list && labelled.parent.GetComponentsInChildren<GuiDropdown>(true).Length == 1)
                labelled = labelled.parent;
            while (item.parent != null && item.parent != list) item = item.parent;
            GameObject row = UnityEngine.Object.Instantiate(labelled.gameObject, list, false);
            row.name = "WiresUpscalers_" + title;
            row.transform.SetSiblingIndex(item.GetSiblingIndex() + 1);
            dropdown = row.GetComponentInChildren<GuiDropdown>(true);
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(options));
            foreach (TMP_Text label in row.GetComponentsInChildren<TMP_Text>(true))
                if (label.GetComponentInParent<GuiDropdown>(true) == null) { label.text = title; break; }
            row.SetActive(false);
            return row;
        }

        private static float Height(GameObject row)
        {
            RectTransform r = row.GetComponent<RectTransform>();
            return r != null ? Mathf.Max(40f, r.rect.height) + 8f : 48f;
        }

        // The current choices onto the menu as it opens (after the game sets its own).
        [HarmonyPatch(typeof(GraphicsSettings), "SetSettingsFromState")]
        [HarmonyPostfix]
        private static void Load(GraphicsSettings __instance)
        {
            if (!Open.TryGetValue(__instance, out Ui ui)) return;
            int at = ui.listed.IndexOf(Plugin.Current);
            if (Plugin.Current != Upscaler.None && at >= 0) ui.upscaling.SetValueWithoutNotify(ui.gameOptions + at);
            ui.model.SetValueWithoutNotify(Math.Max(0, Array.IndexOf(Models, Plugin.DlssModel.Value)));
            int s = 0;
            for (int i = 0; i < Sharp.Length; i++) if (Mathf.Abs(Sharp[i] - Plugin.Sharpness.Value) < .01f) s = i;
            ui.sharpness.SetValueWithoutNotify(s);
            Refresh(ui);
        }

        private static void Refresh(Ui ui)
        {
            Upscaler u = Selected(ui);
            bool every = Plugin.ShowAll.Value;
            bool model = every || u == Upscaler.DLSS, sharp = every || u == Upscaler.FSR3 || u == Upscaler.FSR4;
            if (ui.modelRow.activeSelf == model && ui.sharpRow.activeSelf == sharp) return;
            // the game sizes its list once: grow or shrink it by the rows shown or hidden
            float grow = 0f;
            if (ui.modelRow.activeSelf != model) grow += (model ? 1 : -1) * Height(ui.modelRow);
            if (ui.sharpRow.activeSelf != sharp) grow += (sharp ? 1 : -1) * Height(ui.sharpRow);
            ui.modelRow.SetActive(model);
            ui.sharpRow.SetActive(sharp);
            ui.list.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ui.list.rect.height + grow);
            LayoutRebuilder.MarkLayoutForRebuild(ui.list);
        }

        [HarmonyPatch(typeof(GraphicsSettings), "OnOkAsync")]
        [HarmonyPrefix]
        private static void Save(GraphicsSettings __instance)
        {
            if (!Open.TryGetValue(__instance, out Ui ui)) return;
            Upscaler u = Selected(ui);
            int model = Models[Mathf.Clamp(ui.model.value, 0, Models.Length - 1)];
            float sharp = Sharp[Mathf.Clamp(ui.sharpness.value, 0, Sharp.Length - 1)];
            if (Plugin.UpscalerConfig.Value == (int)u && Plugin.DlssModel.Value == model && Mathf.Approximately(Plugin.Sharpness.Value, sharp)) return;
            Plugin.UpscalerConfig.Value = (int)u;
            Plugin.DlssModel.Value = model;
            Plugin.Sharpness.Value = sharp;
            UpscaleRenderer.SettingsChanged();
        }

        [HarmonyPatch(typeof(GraphicsSettings), "Terminate")]
        [HarmonyPrefix]
        private static void Close(GraphicsSettings __instance) => Open.Remove(__instance);
    }
}
