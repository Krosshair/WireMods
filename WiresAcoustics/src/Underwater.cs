using UnityEngine;

namespace WiresAcoustics
{
    // Water between a sound and your ears (your character's head, Hearing): a sound under the
    // surface (a diving player, their voice, anything below) is quiet while your head is above it,
    // and with your head under, everything above the surface is.
    internal static class Underwater
    {
        // Sound crossing between air and water: the share of its energy that gets through at the
        // surface is 4 Z1 Z2 / (Z1 + Z2)^2 (the textbook transmission at a boundary), with the
        // characteristic impedances of air (about 420 Pa s/m) and water (1.48e6 Pa s/m) (UNSW
        // Physics, Acoustic impedance, intensity and power): about 0.1%, 29.5 dB. The surface takes every
        // frequency alike, so no low-pass.
        private const float ZAir = 420f, ZWater = 1.48e6f;
        internal static readonly float Gain = Mathf.Sqrt(4f * ZAir * ZWater / ((ZAir + ZWater) * (ZAir + ZWater)));
        internal const float Cutoff = 22000f;
        // Surface blending is a function of immersion depth, not elapsed time.
        private static float s_amount;
        internal static bool ListenerBelow;

        // Air-water transmission applied identically to newly started
        // sounds and sounds rechecked by the batched propagation engine.
        internal static void Apply(ref float gain, ref float cutoff)
        {
            Apply(ref gain, ref cutoff, 1f);
        }

        internal static void Apply(ref float gain, ref float cutoff, float amount)
        {
            gain *= Mathf.Lerp(1f, Gain, Mathf.Clamp01(amount));
            cutoff = Mathf.Min(cutoff, Cutoff);
        }

        // Nothing is under water well above the sea (lakes and rivers are at sea level too): skips
        // the game's water lookup (a physics overlap) for almost every sound.
        private static bool High(Vector3 p) => ZoneSystem.instance != null && p.y > ZoneSystem.instance.m_waterLevel + 4f;

        internal static bool Below(Vector3 p) => !High(p) && Floating.GetLiquidLevel(p) > p.y + .05f;

        internal static float SubmergedAmount(Vector3 p)
        {
            if (!Plugin.UnderwaterOn.Value || High(p)) return 0f;
            float depth = Floating.GetLiquidLevel(p) - p.y;
            float x = Mathf.Clamp01(depth / .3f);
            return x * x * (3f - 2f * x);
        }

        // A sound at p, heard from the listener: muffled only when the whole source is under water.
        // Creature sounds play at their feet or middle, so a swimming greydwarf's would count as under
        // water: for a creature (the sound's own, or the one it plays at) its top has to be under.
        // Anything else: 30 cm under, so splashes at the surface stay clear.
        internal static bool Muffles(Vector3 p, Transform source) => SourceAmount(p, source) > 0f;

        // Continuous immersion fraction: a source just touching the surface
        // begins to attenuate, becoming fully water-transmitted after 0.3 m.
        // Characters use the top of the body, not the feet of their sound prefab.
        internal static float SourceAmount(Vector3 p, Transform source)
        {
            if (!Plugin.UnderwaterOn.Value || High(p)) return 0f;
            float depth = Floating.GetLiquidLevel(p) - p.y;
            if (depth <= 0f) return 0f;
            Character c = source != null ? source.GetComponentInParent<Character>() : null;
            if (c == null && depth > .3f) c = CharacterAt(p);
            if (c != null)
            {
                CapsuleCollider body = c.GetComponent<CapsuleCollider>();
                Vector3 top = c.transform.position + Vector3.up;
                if (body != null) { Bounds b = body.bounds; top = new Vector3(b.center.x, b.max.y, b.center.z); }
                depth = Floating.GetLiquidLevel(top) - top.y;
            }
            float fraction = Mathf.Clamp01(depth / .3f);
            // Crossing loss depends on how different the two media are.
            // Both immersed means the source and listener share the medium.
            return fraction * fraction * (3f - 2f * fraction) * (1f - s_amount);
        }

        // The creature a sound plays at (effects are spawned at it, not under it). Only asked for
        // sounds already found under water, so rarely.
        private static Character CharacterAt(Vector3 p)
        {
            Character best = null;
            float bestSq = 2.5f * 2.5f;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null) continue;
                Vector3 d = c.transform.position - p;
                if (Mathf.Abs(d.y) > 3f) continue;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = c; }
            }
            return best;
        }

        // Every frame, from the game's audio update.
        internal static void Update(Vector3 listener, float dt)
        {
            s_amount = SubmergedAmount(listener);
            ListenerBelow = s_amount > .5f;
            AcousticFilter.SetGlobal(Mathf.Lerp(1f, Gain, s_amount), AcousticMath.LogLerp(22000f, Cutoff, s_amount), s_amount > .001f);
        }

        internal static void Reset()
        {
            s_amount = 0f;
            ListenerBelow = false;
            AcousticFilter.SetGlobal(1f, 22000f, false);
        }

        internal static string Summary() => "head under water " + s_amount.ToString("P0");
    }
}
