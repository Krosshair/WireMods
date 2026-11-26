using UnityEngine;

namespace WiresSmoothUI
{
    // Older name for pops, kept so the map pin code reads the same. Every pop
    // goes through UiMotion, so a pop never fights another animation on the
    // same object.
    internal static class MicroMotionSystem
    {
        internal static void Pop(Transform root, float startScale, float peakScale, float duration) =>
            UiMotion.Pop(root, startScale, peakScale, duration);

        internal static void Pulse(Transform root, float peakScale, float duration) =>
            UiMotion.Pop(root, 1f, peakScale, duration);

        internal static void Punch(Transform root, float compression, float peakScale, float duration) =>
            UiMotion.Pop(root, compression, peakScale, duration);
    }
}
