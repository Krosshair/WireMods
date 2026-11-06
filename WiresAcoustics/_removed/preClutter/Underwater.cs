using UnityEngine;

namespace WiresAcoustics
{
    // Water between a sound and your ears. You hear from the camera, not from your character: a sound
    // under the surface (a diving player, their voice, anything below) is dull and quiet while the
    // camera is above it, and everything goes dull if the camera itself is ever under water.
    internal static class Underwater
    {
        internal const float Gain = .45f, Cutoff = 400f;
        private const float Fade = 3f; // per second
        private static float s_amount;
        internal static bool ListenerBelow;

        internal static bool Below(Vector3 p) => Floating.GetLiquidLevel(p) > p.y + .05f;

        // A sound at p, heard from the listener: muffled when only the sound is under water. 30 cm
        // under, so splashes and a swimmer's own sounds at the surface stay clear.
        internal static bool Muffles(Vector3 p) => !ListenerBelow && Floating.GetLiquidLevel(p) > p.y + .3f;

        // Every frame, from the game's audio update.
        internal static void Update(Vector3 listener, float dt)
        {
            ListenerBelow = Below(listener);
            s_amount = Mathf.MoveTowards(s_amount, ListenerBelow ? 1f : 0f, Fade * dt);
            AcousticFilter.SetGlobal(Mathf.Lerp(1f, Gain, s_amount), Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(Cutoff), s_amount)), s_amount > .001f);
        }

        internal static void Reset()
        {
            s_amount = 0f;
            ListenerBelow = false;
            AcousticFilter.SetGlobal(1f, 22000f, false);
        }

        internal static string Summary() => "camera under water " + s_amount.ToString("P0");
    }
}
