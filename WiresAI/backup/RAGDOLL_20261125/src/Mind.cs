using UnityEngine;

namespace WiresAI
{
    // Mind: the three knobs everything else reads, from its personality (Individual), its pack's and
    // its mood (rage, fear, confidence: Behavior).
    //   Pressure: how hard it pushes (attacking sooner, chaining, giving up a chase late).
    //   Wits: how clever it is (reading you, timing, tactics, judging openings, hiding).
    //   Nerve: how brave it is right now (stepping in, holding its ground).
    public sealed partial class WiresAIPlugin
    {
        // Its own aggression pulled toward its pack's, as far as it sticks with the pack.
        private static float GroupAggression(Individual p, Behavior b)
        {
            if (b.Mates <= 0) return p.Aggression;
            return Mathf.Lerp(p.Aggression, b.PackAggression, .5f * Mathf.Clamp01((p.Cohesion + 1f) * .5f));
        }

        // 0..1: aggression (with the pack's), confidence and anger.
        private static float Pressure(Individual p, Behavior b) =>
            Mathf.Clamp01(.5f * (GroupAggression(p, b) + 1f) * .6f + .25f * Mathf.Clamp01(b.Confidence) + .15f * b.Rage);

        // 0..1: its tactics trait.
        private static float Wits(Individual p) => Mathf.Clamp01((p.Tactics + 1f) * .5f);

        // 0..1: courage (with the pack's), confidence up, fear down.
        private static float Nerve(Individual p, Behavior b)
        {
            float courage = b.Mates > 0 ? Mathf.Lerp(p.Courage, b.PackCourage, .5f * Mathf.Clamp01((p.Cohesion + 1f) * .5f)) : p.Courage;
            return Mathf.Clamp01(.5f * (courage + 1f) + .25f * Mathf.Clamp01(b.Confidence) - .25f * Mathf.Clamp01(b.Fear));
        }

        // Dim ones keep vanilla's circling: the further below average wits, the likelier (fixed per creature).
        private static bool KeepsCircling(Individual p) => p.Tactics < 0f && Roll(p, 0x51C1u) < -p.Tactics;
    }
}
