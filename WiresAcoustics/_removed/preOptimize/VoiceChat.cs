using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Proximity Voice Chat (Azumatt): each player's voice is its own AudioSource, not a game sound.
    // Here it's handled like every other sound: our HRTF, and our muffling through walls (layers,
    // checked with the room rays' timing). Its own muffling (occlusion, underwater) is switched off
    // completely: its low-pass stays open and its muffle volume at 1. Global (positionless) voices
    // keep its normal panning and get no muffling. Its reverb option is left as it is.
    internal static class VoiceChat
    {
        private const string Stream = "ProximityVoiceChat.Voice.Audio.PeerVoiceStream";
        private static FieldInfo s_source;

        [HarmonyPatch]
        private static class Build
        {
            private static bool Prepare() => TargetMethod() != null;

            private static MethodBase TargetMethod()
            {
                System.Type t = AccessTools.TypeByName(Stream);
                if (t == null) return null;
                s_source = AccessTools.Field(t, "_source");
                return s_source != null ? AccessTools.Method(t, "BuildSource") : null;
            }

            private static void Postfix(object __instance)
            {
                if (!(s_source.GetValue(__instance) is AudioSource src) || src == null) return;
                AcousticFilter f = src.GetComponent<AcousticFilter>() ?? src.gameObject.AddComponent<AcousticFilter>();
                f.FollowBlend = true; // HRTF set from the next frame on, by its blend and the setting
                f.Voice = true;       // muffled by Room's tick
                Plugin.Log.LogInfo("[acoustics] voice chat: " + src.name + " handled here (HRTF, muffling)");
            }
        }

        // Its muffle: always fully open while ours is on.
        [HarmonyPatch]
        private static class NoMuffle
        {
            private static bool Prepare() => TargetMethod() != null;

            private static MethodBase TargetMethod()
            {
                System.Type t = AccessTools.TypeByName(Stream);
                return t != null ? AccessTools.Method(t, "SetMuffle") : null;
            }

            private static void Prefix(ref float targetCutoff, ref float targetVolume)
            {
                if (!Plugin.Enabled.Value) return;
                targetCutoff = 22000f;
                targetVolume = 1f;
            }
        }
    }
}
