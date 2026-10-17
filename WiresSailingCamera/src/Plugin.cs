using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WiresSailingCamera
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.sailingcamera";
        public const string Name = "Wire's Sailing Camera";
        public const string Version = "0.2.2";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> HeightOffset;
        internal static ConfigEntry<float> MaximumZoom;

        private Harmony _harmony;

        private void Awake()
        {
            Enabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enable the sailing-only camera height adjustment.");

            HeightOffset = Config.Bind(
                "Sailing Camera",
                "Height Offset",
                7.5f,
                new ConfigDescription(
                    "Extra world-space camera pivot height in metres while steering a ship. Higher positions place the ship and character below the crosshair rather than near its centre. Vanilla camera distance and sideways position remain unchanged.",
                    new AcceptableValueRange<float>(0f, 20f)));

            MaximumZoom = Config.Bind(
                "Sailing Camera",
                "Maximum Zoom",
                30f,
                new ConfigDescription(
                    "Maximum third-person camera distance in metres while steering a ship. Vanilla is 6 m. Default 30 m for a much farther-back view; scroll back to zoom out. Does not change the camera height or on-foot view.",
                    new AcceptableValueRange<float>(6f, 60f)));

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded (height + extended boat zoom).");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchSelf();
        }
    }

    [HarmonyPatch(typeof(GameCamera), "GetCameraBaseOffset")]
    internal static class SailingHeightPatch
    {
        // Existing Valheim third-person camera distance flag.
        private static readonly AccessTools.FieldRef<GameCamera, float>
            Distance =
                AccessTools.FieldRefAccess<GameCamera, float>(
                    "m_distance");

        [HarmonyPostfix]
        private static void Postfix(
            GameCamera __instance,
            Player player,
            ref Vector3 __result)
        {
            if (Plugin.Enabled == null ||
                !Plugin.Enabled.Value ||
                __instance == null ||
                player == null ||
                player != Player.m_localPlayer ||
                player.GetControlledShip() == null ||
                Distance(__instance) <= 0.05f ||
                GameCamera.InFreeFly())
            {
                return;
            }

            // Valheim's own UpdateBaseOffset smoothly eases this pivot
            // adjustment over 0.5 seconds. No camera side shift,
            // sail renderer or animation changes.
            __result += Vector3.up * Plugin.HeightOffset.Value;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    internal static class SailingZoomPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            GameCamera __instance,
            out float __state)
        {
            // Change the limit only during vanilla's own camera update
            // and restore it immediately afterwards. The scroll handling
            // in LateUpdate performs the actual zoom.
            __state = __instance.m_maxDistanceBoat;

            if (Plugin.Enabled == null ||
                !Plugin.Enabled.Value)
                return;

            Player player = Player.m_localPlayer;
            if (player == null ||
                player.GetControlledShip() == null ||
                GameCamera.InFreeFly())
                return;

            __instance.m_maxDistanceBoat =
                Mathf.Max(__state, Plugin.MaximumZoom.Value);
        }

        [HarmonyPostfix]
        private static void Postfix(
            GameCamera __instance,
            float __state)
        {
            __instance.m_maxDistanceBoat = __state;
        }

        [HarmonyFinalizer]
        private static System.Exception Finalizer(
            GameCamera __instance,
            float __state,
            System.Exception __exception)
        {
            // Also restore the original zoom limit if another patch
            // throws during LateUpdate.
            __instance.m_maxDistanceBoat = __state;
            return __exception;
        }
    }}
