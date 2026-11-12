using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Inside the Mistlands mist the game lowers a sound's pitch with distance (down to half at 64 m)
    // and takes its reverb away. Fog doesn't do that to sound: water droplets that small barely touch
    // it, and the damp air if anything carries the highs a little further. So in the mist sounds are
    // left as anywhere else: the game's pitch drop is undone and the distance reverb it removed is put
    // back (the same formula it uses outside the mist), before this mod's own reverb send, air and
    // walls apply as usual.
    internal static class Mist
    {
        private static readonly AccessTools.FieldRef<ZSFX, float> PitchDrop = AccessTools.FieldRefAccess<ZSFX, float>("m_reverbPitchModifier");

        // From Occlusion, right after the game's reverb update for a placed sound.
        internal static void Undo(ZSFX z, AudioSource src, float dist)
        {
            if (PitchDrop(z) <= 0f) return; // only set inside the mist
            PitchDrop(z) = 0f;              // the pitch itself is set from it every frame (ZSFX.CustomUpdate)
            if (!z.m_distanceReverb || src.bypassReverbZones) return;
            float range = z.m_useCustomReverbDistance ? z.m_customReverbDistance : 64f;
            float near = Mathf.Clamp01(dist / range);
            float far = Mathf.Clamp01(src.maxDistance / range) * Mathf.Clamp01(dist / Mathf.Max(.01f, src.maxDistance));
            src.reverbZoneMix = Mathf.Max(near, far);
        }
    }
}
