using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // The Doppler effect: a sound coming toward you is higher, going away lower, by c / (c + v) with
    // v how fast the distance from your character's head to it is growing. The speed is worked out on
    // the worker threads with the directions (Directions.Job: smoothed, capped at 50 m/s, a teleport
    // skipped); the main thread only puts the factor on top of the pitch the game sets for the sound
    // each frame. Unity's own Doppler stays off for these sounds.
    internal static class Doppler
    {
        // The speed of sound for the mod: 350 m/s instead of air's 343, so the frame a new sound
        // waits for its wall check (Starts) is part of its travel time rather than on top of it.
        internal const float Speed = 350f;
        internal const float MaxSpeed = 50f, Jump = 2f;

        private static readonly AccessTools.FieldRef<ZSFX, float> BasePitch = AccessTools.FieldRefAccess<ZSFX, float>("m_basePitch");
        private static readonly AccessTools.FieldRef<ZSFX, float> PitchModifier = AccessTools.FieldRefAccess<ZSFX, float>("m_pitchModifier");
        private static readonly AccessTools.FieldRef<ZSFX, float> MistPitch = AccessTools.FieldRefAccess<ZSFX, float>("m_reverbPitchModifier");

        // Sounds outside the sound-effect system (a ship's wake and water, a shield's hum): nothing
        // sets their pitch every frame, so the shift goes on the pitch they had. If the game changes
        // it in between, that becomes the new base.
        internal static void ApplyOther(AcousticFilter f)
        {
            AudioSource src = f.Source;
            if (src == null) return;
            if (!f.OtherPitchKnown || Mathf.Abs(src.pitch - f.OtherWritten) > 1e-4f)
            {
                f.OtherBase = src.pitch;
                f.OtherPitchKnown = true;
            }
            float want = f.OtherBase * f.Doppler;
            if (Mathf.Abs(want - src.pitch) < 1e-4f) return;
            src.pitch = want;
            f.OtherWritten = want;
        }

        // Release destroyed filters from the lookup without clearing live
        // entries in a burst every 2048 sounds.
        internal static void Forget(AcousticFilter f)
        {
            if (ReferenceEquals(f, null)) return;
            ZSFX z = f.GetComponent<ZSFX>();
            if (z != null) Apply.Forget(z.GetInstanceID(), f);
        }

        // The game sets the pitch every frame (its random pitch, its modifiers); ours goes on top.
        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.CustomUpdate))]
        private static class Apply
        {
            // Each sound's filter looked up once; a sound with no shift leaves before any engine call.
            private static readonly System.Collections.Generic.Dictionary<int, AcousticFilter> Filters =
                new System.Collections.Generic.Dictionary<int, AcousticFilter>(256);

            internal static void Forget(int id, AcousticFilter f)
            {
                if (Filters.TryGetValue(id, out AcousticFilter existing) &&
                    ReferenceEquals(existing, f)) Filters.Remove(id);
            }

            private static void Postfix(ZSFX __instance, AudioSource ___m_audioSource)
            {
                int id = __instance.GetInstanceID();
                if (!Filters.TryGetValue(id, out AcousticFilter f) || f == null)
                {
                    f = __instance.GetComponent<AcousticFilter>();
                    if (f == null) return; // not cached: a filter can still be added to it later
                    // Sound destruction removes its own entry (Forget).
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
