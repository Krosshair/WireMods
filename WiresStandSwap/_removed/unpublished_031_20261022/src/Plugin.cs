using BepInEx;
using HarmonyLib;
using BepInEx.Logging;
using BepInEx.Configuration;
using UnityEngine;

namespace WiresStandSwap
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.standswap";
        public const string Name = "Wire's Stand Swap";
        public const string Version = "0.3.1";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyCode> MainHoldKey;
        internal static ConfigEntry<KeyCode> FocusModifier;
        internal static ConfigEntry<float> HoldSeconds;
        private Harmony _harmony;

        private void Awake()
        {
            MainHoldKey = Config.Bind("1- Controls", "Stand Interaction Hold Key",
                KeyCode.E,
                "Briefly hold this key over a stand for the smart full outfit / held item interaction. Tap normally for vanilla action, or use Shift+E for a specific piece.");
            FocusModifier = Config.Bind("1- Controls", "Focused Swap Modifier",
                KeyCode.LeftShift,
                "Hold this modifier and press the Stand Interaction Key once on an armor-stand piece for an exact single-slot swap. Default Shift+E. R is always normal vanilla hide/show.");
            HoldSeconds = Config.Bind("1- Controls", "Short Hold Duration",
                0.22f,
                new ConfigDescription("Seconds required to activate Hold E. The focused Shift+E action activates immediately. No inventory or scene searches are performed every frame.",
                    new AcceptableValueRange<float>(0.12f, 0.65f)));

            Instance = this;
            Log = Logger;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Instance = null;
            Log = null;
            InputAndEquipment.Clear();
            StandSwaps.Clear();
            ArmorLoadouts.Clear();
            StandRegistry.Clear();
            UsedItems.Clear();
        }
    }

}
