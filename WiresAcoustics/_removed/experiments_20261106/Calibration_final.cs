using UnityEngine;

namespace WiresAcoustics
{
    // How loud Unity's reverb actually is, measured in game (a noise burst played dry and into the
    // reverb, the game's final output summed): the physics says how much reverberant energy a sound
    // should have next to its direct energy, and this turns that into Unity's reverb send.
    internal static class Calibration
    {
        // The reverb unit's setting the measurements were made at (and Room keeps it there).
        internal const float Decay = 1f, Late = 1000f, Reflections = 0f;

        // At a send of 1 the reverb carried 10.4 times the direct energy (+10.2 dB, the same 25 m
        // away: Unity takes it after the distance fade), and it scales with the send squared.
        private const float FullSend = 10.4f, Power = 2f;
        // Unity's tail gains little energy from a longer decay: 2 s at -3 dB measured 2.6 dB under
        // 1 s, so energy ~ decay ^ 0.13 and the start is lowered by that much to keep it the same.
        internal const float TailExponent = .13f;

        // The send that gives a reverberant / direct energy ratio of ratio^2.
        internal static float MixFor(float ratio)
        {
            float energy = ratio * ratio / FullSend;
            return Mathf.Clamp(Mathf.Pow(energy, 1f / Power), 0f, 1f);
        }
    }
}
