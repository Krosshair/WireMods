using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace WiresNativeCursorSupport
{
    internal static class AccessibilityIntegration
    {
        private static readonly FieldInfo ToggleBlockField =
            AccessTools.Field(
                typeof(AccessibilitySettings),
                "m_toggleBlockToggle");

        private static AccessibilitySettings _owner;
        private static Toggle _toggle;
        private static bool _openedValue;

        internal static void Initialize(
            AccessibilitySettings settings)
        {
            if (settings == null ||
                ToggleBlockField == null)
                return;

            Toggle source =
                ToggleBlockField.GetValue(settings)
                    as Toggle;

            if (source == null ||
                source.transform.parent == null)
                return;

            if (_owner != settings ||
                _toggle == null)
            {
                _owner = settings;
                _toggle =
                    FindOrCreateToggle(source);
            }

            if (_toggle == null)
                return;

            _openedValue =
                Plugin.RuntimeEnabled;

            _toggle.SetIsOnWithoutNotify(
                _openedValue);

            // A cloned Valheim Toggle also clones the source row's
            // serialized ToggleBlock callbacks. Replace the event
            // entirely so this row controls only Native Cursor.
            _toggle.onValueChanged =
                new Toggle.ToggleEvent();

            _toggle.onValueChanged.AddListener(
                delegate(bool value)
                {
                    Plugin.SetRuntimeEnabled(
                        value);
                });

            SetLabel(_toggle);
        }

        private static Toggle FindOrCreateToggle(
            Toggle source)
        {
            Transform parent =
                source.transform.parent;

            Transform existing =
                parent.Find(
                    "WireNativeCursor");

            if (existing != null)
                return existing.GetComponent<Toggle>();

            GameObject row =
                Object.Instantiate(
                    source.gameObject,
                    parent,
                    false);

            row.name =
                "WireNativeCursor";

            row.transform.SetSiblingIndex(
                source.transform
                    .GetSiblingIndex() + 1);

            Toggle toggle =
                row.GetComponent<Toggle>();

            if (toggle != null)
                SetLabel(toggle);

            return toggle;
        }

        private static void SetLabel(
            Toggle toggle)
        {
            if (toggle == null)
                return;

            TMP_Text label =
                toggle.transform
                    .Find("Label")
                    ?.GetComponent<TMP_Text>();

            if (label == null)
            {
                label =
                    toggle.GetComponentInChildren<
                        TMP_Text>(true);
            }

            if (label != null)
                label.text =
                    "Native System Cursor";
        }

        internal static void Save()
        {
            if (_toggle == null)
                return;

            bool enabled =
                _toggle.isOn;

            PlayerPrefs.SetInt(
                Plugin.PrefKey,
                enabled ? 1 : 0);
            PlayerPrefs.Save();

            _openedValue = enabled;

            Plugin.SetRuntimeEnabled(
                enabled);
        }

        internal static void Cancel()
        {
            if (_toggle != null)
            {
                _toggle.SetIsOnWithoutNotify(
                    _openedValue);
            }

            Plugin.SetRuntimeEnabled(
                _openedValue);
        }

        internal static void ConfigureNavigation(
            AccessibilitySettings settings,
            Button okButton)
        {
            if (settings == null ||
                okButton == null ||
                _toggle == null ||
                ToggleBlockField == null)
                return;

            Toggle block =
                ToggleBlockField.GetValue(settings)
                    as Toggle;

            if (block == null)
                return;

            GuiUtils.SetNavigationDown(
                block,
                _toggle);

            GuiUtils.SetNavigationUp(
                _toggle,
                block);

            GuiUtils.SetNavigationDown(
                _toggle,
                okButton);

            GuiUtils.SetNavigationUp(
                okButton,
                _toggle);
        }
    }

    [HarmonyPatch(
        typeof(AccessibilitySettings),
        nameof(AccessibilitySettings.Initialize))]
    internal static class AccessibilityInitializePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            AccessibilitySettings __instance)
        {
            AccessibilityIntegration.Initialize(
                __instance);
        }
    }

    [HarmonyPatch(
        typeof(AccessibilitySettings),
        nameof(AccessibilitySettings.OnOkAsync))]
    internal static class AccessibilityOkPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            AccessibilityIntegration.Save();
        }
    }

    [HarmonyPatch(
        typeof(AccessibilitySettings),
        nameof(AccessibilitySettings.OnBack))]
    internal static class AccessibilityBackPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            AccessibilityIntegration.Cancel();
        }
    }

    [HarmonyPatch(
        typeof(AccessibilitySettings),
        nameof(AccessibilitySettings.OnTabOpen))]
    internal static class AccessibilityTabOpenPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            AccessibilitySettings __instance,
            Button okButton)
        {
            AccessibilityIntegration
                .ConfigureNavigation(
                    __instance,
                    okButton);
        }
    }
}
