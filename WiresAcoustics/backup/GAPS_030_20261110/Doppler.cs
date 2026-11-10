using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // The Doppler effect: a sound coming toward you is higher, going away lower, by 343 / (343 + v)
    // with v how fast the distance between it and you is growing. Worked out every frame from the
    // distances Directions already reads (smoothed, capped at 50 m/s), and applied on top of the
    // pitch the game sets for the sound every frame. Unity's own Doppler is switched off for these
    // sounds (it also reacts to the camera jumping when it bumps into things: a pitch blip on every
    // sound); a camera jump here is simply skipped.
    internal static class Doppler
    {
        private static readonly AccessTools.FieldRef<ZSFX, float> BasePitch = AccessTools.FieldRefAccess<ZSFX, float>("m_basePitch");
        private static readonly AccessTools.FieldRef<ZSFX, float> PitchModifier = AccessTools.FieldRefAccess<ZSFX, float>("m_pitchModifier");
        private static readonly AccessTools.FieldRef<ZSFX, float> MistPitch = AccessTools.FieldRefAccess<ZSFX, float>("m_reverbPitchModifier");
        private const float MaxSpeed = 50f, Jump = 2f;
        private static Vector3 s_lastListener;
        private static bool s_jumped;

        // Once a frame before the filters are updated (Directions).
        internal static void Frame(Vector3 listener)
        {
            s_jumped = (listener - s_lastListener).sqrMagnitude > Jump * Jump;
            s_lastListener = listener;
        }

        // Per sound, from Directions: its new distance.
        internal static void Update(AcousticFilter f, float dist, float dt)
        {
            if (f.PrevDist < 0f || s_jumped || dt <= 1e-4f || !Plugin.RealDistance.Value) { f.PrevDist = dist; return; }
            float v = Mathf.Clamp((dist - f.PrevDist) / dt, -MaxSpeed, MaxSpeed);
            f.PrevDist = dist;
            f.RadialSpeed = Mathf.Lerp(f.RadialSpeed, v, .2f);
            f.Doppler = Travel.Speed / (Travel.Speed + f.RadialSpeed);
        }

        // The game sets the pitch every frame (its random pitch, its modifiers); ours goes on top.
        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.CustomUpdate))]
        private static class Apply
        {
            // Every sound, every frame: the filter is looked up once per sound, and a sound with no
            // shift (anything not moving toward or away from you) leaves before any engine call.
            private static readonly System.Collections.Generic.Dictionary<int, AcousticFilter> Filters =
                new System.Collections.Generic.Dictionary<int, AcousticFilter>(256);

            private static void Postfix(ZSFX __instance, AudioSource ___m_audioSource)
            {
                int id = __instance.GetInstanceID();
                if (!Filters.TryGetValue(id, out AcousticFilter f) || f == null)
                {
                    f = __instance.GetComponent<AcousticFilter>();
                    if (f == null) return; // not cached: a filter can still be added to it later
                    if (Filters.Count > 2048) Filters.Clear(); // ids of sounds long gone
                    Filters[id] = f;
                }
                if (Mathf.Abs(f.Doppler - 1f) < 1e-3f) return;
                AudioSource src = ___m_audioSource;
                if (src == null || !src.isPlaying) return;
                float p = BasePitch(__instance) * PitchModifier(__instance);
                p -= p * MistPitch(__instance);
                src.pitch = p * f.Doppler;
            }
        }
    }
}
