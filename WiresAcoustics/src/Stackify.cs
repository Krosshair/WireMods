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
            private static void Prefix() => s_active = Plugin.Enabled.Value;
            private static void Finalizer() { s_active = false; s_group = null; }
        }

        [HarmonyPatch]
        private static class Near
        {
            private static bool Prepare() => AccessTools.TypeByName(Type) != null;
            private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName(Type), "Near");
            private static void Postfix(Vector3 source, ref Vector3 __result)
            {
                if (s_active) __result = source;
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
