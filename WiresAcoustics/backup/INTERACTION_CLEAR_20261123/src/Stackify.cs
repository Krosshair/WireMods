using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace WiresAcoustics
{
    // Wire's Stackify plays a chest's opening sound for each chest a quick stack or restock used. On
    // its own it places that sound 1.5 m from the camera toward the chest, on the interface channel,
    // so it's as loud as when you're right at the chest and the game's distance reverb stays off it.
    // With this mod the sound comes from the chest itself, on its own world channel: heard through
    // walls and openings, with the room's reverb and the HRTF, like the chest opening in person.
    internal static class Stackify
    {
        private const string Type = "WiresStackify.StackifyEffects";
        private static bool s_active;
        private static Container s_chest;
        private static AudioMixerGroup s_group;

        internal static bool Active => s_active;

        // From Occlusion, as the sound starts (inside Stackify's call): back on its world channel.
        internal static void Restore(AudioSource src)
        {
            if (s_group != null) src.outputAudioMixerGroup = s_group;
        }

        [HarmonyPatch]
        private static class Opening
        {
            private static bool Prepare() => AccessTools.TypeByName(Type) != null;
            private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName(Type), "PlayContainerOpening");
            private static void Prefix(Container container)
            {
                s_active = Plugin.Enabled.Value;
                s_chest = s_active ? container : null;
            }
            private static void Finalizer()
            {
                s_active = false; s_group = null; s_chest = null;
            }
        }

        [HarmonyPatch]
        private static class Near
        {
            private static bool Prepare() => AccessTools.TypeByName(Type) != null;
            private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName(Type), "Near");
            private static void Postfix(Vector3 source, ref Vector3 __result)
            {
                if (s_active)
                {
                    // Restore a real container's audible opening to its lid;
                    // source occlusion is then evaluated across exposed faces.
                    Vector3 lid;
                    __result = s_chest != null &&
                        Area.TopOf(s_chest.transform, out lid) ? lid : source;
                }
            }
        }

        // Stackify instantiates the ZSFX itself, outside EffectList.Create.
        // Give its new chest sounds the same owner/surface semantics before
        // ZSFX.UpdateReverb and before any opening-ray jobs are scheduled.
        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
        private static class ChestPlay
        {
            private static void Prefix(ZSFX __instance)
            {
                if (!s_active || s_chest == null || __instance == null) return;
                AudioSource audio = __instance.GetComponent<AudioSource>();
                if (audio == null || audio.spatialBlend < .5f) return;
                AcousticFilter f = AcousticFilter.For(audio);
                f.ShapeRoot = s_chest.transform;
                f.AreaKnown = false;
                f.LocalizedEvent = false;
                f.SurfaceEmitter = true;
            }
        }

        [HarmonyPatch]
        private static class PlayNow
        {
            private static bool Prepare() => AccessTools.TypeByName(Type) != null;
            private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName(Type), "PlayNow");
            private static void Prefix(GameObject prefab)
            {
                if (!s_active) return;
                AudioSource src = prefab != null ? prefab.GetComponentInChildren<AudioSource>(true) : null;
                s_group = src != null ? src.outputAudioMixerGroup : null;
            }
        }
    }
}
