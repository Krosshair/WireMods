using System.Collections;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace WiresInstafill
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.instafill";
        public const string Name = "Wire's Instafill";
        public const string Version = "1.0.1";

        internal static Plugin Instance;
        internal bool IsFilling;
        // Suppresses only two harmless vanilla smelter-success
        // diagnostics while Instafill is rapidly inserting items.
        // Never suppresses warnings, errors or other ZLog messages.
        private float _bulkSmelterLogUntil;
        internal bool ShouldSkipSmelterSuccessLog(object message)
        {
            if (message == null ||
                Time.realtimeSinceStartup >= _bulkSmelterLogUntil)
                return false;

            string text = message as string;
            if (text == null)
                return false;

            return text.StartsWith("trying to add ",
                    System.StringComparison.Ordinal) ||
                   text.StartsWith("Added ore ",
                    System.StringComparison.Ordinal);
        }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        internal void BeginFill(
            Player player,
            GameObject hover,
            bool alt)
        {
            if (IsFilling ||
                player == null ||
                hover == null)
                return;

            Interactable interactable =
                hover.GetComponentInParent<Interactable>();

            MonoBehaviour behaviour =
                interactable as MonoBehaviour;

            if (interactable == null ||
                behaviour == null)
                return;

            StartCoroutine(
                FillRoutine(
                    player,
                    interactable,
                    behaviour,
                    alt,
                    hover.GetComponentInParent<Smelter>() != null));
        }

        private IEnumerator FillRoutine(
            Player player,
            Interactable interactable,
            MonoBehaviour behaviour,
            bool alt,
            bool smelter)
        {
            IsFilling = true;

            try
            {
                // One normal vanilla interaction per rendered frame.
                // This deliberately avoids blasting a full machine's worth
                // of RPCs into the network in a single frame.
                for (int i = 0; i < 1024; i++)
                {
                    if (player == null ||
                        behaviour == null ||
                        player.IsDead())
                        yield break;

                    if (smelter)
                        _bulkSmelterLogUntil = Time.realtimeSinceStartup + 1f;

                    bool used =
                        interactable.Interact(
                            player,
                            false,
                            alt);

                    if (!used)
                        yield break;

                    yield return null;
                }
            }
            finally
            {
                IsFilling = false;
            }
        }

        private void OnDestroy()
        {
            StopAllCoroutines();

            if (_harmony != null)
                _harmony.UnpatchSelf();

            Instance = null;
        }
    }

    [HarmonyPatch(typeof(Player), "Interact")]
    internal static class ShiftUseInstafillPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            Player __instance,
            GameObject go,
            bool hold,
            bool alt)
        {
            if (__instance == null ||
                go == null ||
                Plugin.Instance == null ||
                !IsShiftHeld() ||
                !IsSupportedRefillTarget(go))
                return true;

            if (Plugin.Instance.IsFilling)
                return false;

            if (hold)
                return false;

            Plugin.Instance.BeginFill(
                __instance,
                go,
                alt);

            return false;
        }

        private static bool IsShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) ||
                   Input.GetKey(KeyCode.RightShift);
        }

        private static bool IsSupportedRefillTarget(
            GameObject go)
        {
            return
                go.GetComponentInParent<Smelter>() != null ||
                go.GetComponentInParent<Fireplace>() != null ||
                go.GetComponentInParent<CookingStation>() != null ||
                go.GetComponentInParent<Fermenter>() != null ||
                go.GetComponentInParent<Turret>() != null ||
                go.GetComponentInParent<ShieldGenerator>() != null ||
                go.GetComponentInParent<Catapult>() != null;
        }
    }

    // Restrict log filtering to a single active Instafill smelter
    // transfer. These are vanilla INFO messages, not errors.
    [HarmonyPatch(typeof(ZLog), nameof(ZLog.Log),
        new[] { typeof(object) })]
    internal static class SmelterBulkLogFilter
    {
        [HarmonyPrefix]
        private static bool Prefix(object o)
        {
            Plugin plugin = Plugin.Instance;
            return plugin == null ||
                !plugin.ShouldSkipSmelterSuccessLog(o);
        }
    }
}
