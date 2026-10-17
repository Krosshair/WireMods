using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WiresStableFirelight
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class StableFirelightPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "wire.valheim.stablefirelight";
        public const string PluginName = "Wire's Stable Firelight";
        public const string PluginVersion = "1.0.0";

        internal static ConfigEntry<float> MovementReductionPercent;

        private Harmony _harmony;

        private void Awake()
        {
            MovementReductionPercent = Config.Bind(
                "General",
                "Light movement reduction",
                90f,
                new ConfigDescription(
                    "How much positional light movement to remove. 0 = vanilla movement, 100 = completely stationary light position.",
                    new AcceptableValueRange<float>(0f, 100f)));

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            Logger.LogInfo(
                PluginName + " " + PluginVersion +
                " loaded. Light movement reduction: " +
                MovementReductionPercent.Value + "%.");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchSelf();
        }

        [HarmonyPatch(typeof(LightFlicker), "Awake")]
        private static class LightFlickerAwakePatch
        {
            private static void Postfix(LightFlicker __instance)
            {
                if (__instance == null)
                    return;

                // Only stationary fuel-burning world lights.
                if (__instance.GetComponentInParent<Fireplace>() == null)
                    return;

                float reduction =
                    Mathf.Clamp(
                        MovementReductionPercent.Value,
                        0f,
                        100f);

                __instance.m_movement *=
                    1f - reduction / 100f;
            }
        }
    }
}
