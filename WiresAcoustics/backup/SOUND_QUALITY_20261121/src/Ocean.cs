using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Ocean is an environmental field, not a handful of manufactured
    // shoreline emitters. Keep Valheim's existing loop, spatialization,
    // distance, volume and clip handling. The existing room measurements
    // only supply shelter-dependent tonal shaping at a bounded cadence.
    [HarmonyPatch(typeof(AudioMan), "UpdateOceanAmbiance")]
    internal static class Ocean
    {
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> Source =
            AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_oceanAmbientSource");
        private const float Interval = .25f;
        private static AudioSource s_original;
        private static float s_next, s_gain = 1f, s_cutoff = 22000f;
        private static int s_updates;

        private static void Postfix(AudioMan __instance)
        {
            if (!Plugin.Enabled.Value)
            {
                Shutdown();
                return;
            }
            AudioSource src = Source(__instance);
            if (src == null) return;
            if (s_original != src)
            {
                Shutdown();
                s_original = src;
                s_next = 0f;
            }
            if (!src.isPlaying || src.volume <= .001f) return;
            if (Time.time < s_next) return;
            s_next = Time.time + Interval;

            // A single shared room observation describes how much the
            // building shelters the listener from outside ocean noise.
            // It never changes the sound's source position or lifetime.
            float shelter = Mathf.Clamp01(Room.Enclosure);
            float exposure = 1f;
            Vector3 from = src.transform.position - Hearing.At;
            if (from.sqrMagnitude > 1f)
                exposure = Room.Exposure(from.normalized, out _);
            shelter *= 1f - .5f * Mathf.Clamp01(exposure);
            float targetGain = Mathf.Lerp(1f, .72f, shelter);
            float targetCut = Mathf.Exp(Mathf.Lerp(
                Mathf.Log(22000f), Mathf.Log(5500f), shelter));
            s_gain = Mathf.Lerp(s_gain, targetGain, .3f);
            s_cutoff = Mathf.Exp(Mathf.Lerp(
                Mathf.Log(s_cutoff), Mathf.Log(targetCut), .3f));

            AcousticFilter f = src.GetComponent<AcousticFilter>();
            if (f == null) f = src.gameObject.AddComponent<AcousticFilter>();
            f.Kind = AcousticFilter.Kinds.Ocean;
            f.Placed = false;
            f.Set(s_gain, s_cutoff, false);
            Budget.Note(f, from.magnitude);
            s_updates++;
        }

        internal static void Shutdown()
        {
            if (s_original != null)
            {
                AcousticFilter f = s_original.GetComponent<AcousticFilter>();
                if (f != null)
                {
                    f.Set(1f, 22000f, false);
                    f.Active = false;
                }
            }
            s_original = null;
            s_next = 0f;
            s_gain = 1f;
            s_cutoff = 22000f;
            s_updates = 0;
        }

        internal static void Dispose() => Shutdown();

        internal static string Summary() =>
            "ocean: native loop, no shoreline copies, shelter " +
            s_gain.ToString("F2") + ", lowpass " +
            s_cutoff.ToString("F0") + "Hz, updates " + s_updates;
    }
}
