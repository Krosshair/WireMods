using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WiresHoodToggle
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.hoodtoggle";
        public const string Name = "Wire's Helmet Hider";
        public const string Version = "0.2.4";

        internal static ConfigEntry<KeyboardShortcut> CustomToggleKey;
        internal static ConfigEntry<float> DoubleTapWindow;
        internal static ConfigEntry<float> EmoteHoldDuration;
        internal static ConfigEntry<bool> PlayEquipAnimation;
        internal static ConfigEntry<float> AnimationDuration;
        internal static ConfigEntry<bool> ResetOnHeadgearChange;

        private Harmony _harmony;
        internal static ManualLogSource Log;

        private void Awake()
        {
            CustomToggleKey = Config.Bind(
                "Input",
                "Custom Toggle Key",
                new KeyboardShortcut(KeyCode.None),
                "Optional direct toggle key. Leave None to use the default emote-button double tap.");

            DoubleTapWindow = Config.Bind(
                "Input",
                "Double Tap Window",
                0.3f,
                new ConfigDescription(
                    "Maximum time between two taps when using the emote button.",
                    new AcceptableValueRange<float>(0.15f, 0.6f)));

            EmoteHoldDuration = Config.Bind(
                "Input",
                "Emote Hold Duration",
                0.33f,
                new ConfigDescription(
                    "How long the emote button must be held before the emote radial opens when using the default emote-button control.",
                    new AcceptableValueRange<float>(0.15f, 1f)));

            PlayEquipAnimation = Config.Bind("Animation", "Play equip animation", true,
                "Play a short, synced visual-only equipping animation when toggling helmet visibility. No equipment action, movement restriction or delay beyond the visual.");
            AnimationDuration = Config.Bind("Animation", "Animation duration", 0.22f,
                new ConfigDescription("Maximum time to play the cosmetic equip animation before applying the visual change. Cancelling the animation applies the change immediately.",
                    new AcceptableValueRange<float>(0.08f, 0.4f)));
            ResetOnHeadgearChange = Config.Bind("Visibility", "Reset hidden state on headgear change", true,
                "Show the headpiece again when actually equipping, unequipping or swapping headgear. Disable to keep the hidden state until manually toggled.");

            Log = Logger;
            SavedHelmetStates.Load();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void Update()
        {
            HoodVisuals.UpdateLocal();
            HoodInput.Update();
        }

        private void OnDestroy()
        {
            HoodInput.Reset();
            HelmetToggleAnimation.Cancel(commit: false);
            HoodVisuals.Clear();
            _harmony?.UnpatchSelf();
        }
    }

    [HarmonyPatch(typeof(Player), "CheckKeyboardRadialPressed")]
    internal static class EmoteButtonHoldPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (!HoodInput.UsingEmoteButton)
                return true;

            if (ZInput.GetButtonDown("OpenRadial"))
            {
                __result = true;
                return false;
            }

            __result = HoodInput.ShouldOpenEmoteRadial();
            return false;
        }
    }

    [HarmonyPatch(typeof(VisEquipment), "CustomUpdate")]
    internal static class VisEquipmentUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(VisEquipment __instance)
        {
            HoodVisuals.Apply(__instance);
        }
    }

    [HarmonyPatch(typeof(VisEquipment), "GetHairItem")]
    internal static class HairVisibilityPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            VisEquipment __instance,
            int itemHash,
            ref int __result)
        {
            if (HoodVisuals.IsHidden(__instance))
                __result = itemHash;
        }
    }
}
