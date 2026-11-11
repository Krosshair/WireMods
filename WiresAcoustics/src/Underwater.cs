using UnityEngine;

namespace WiresAcoustics
{
    // Water between a sound and your ears. You hear from the camera, not from your character: a sound
    // under the surface (a diving player, their voice, anything below) is dull and quiet while the
    // camera is above it, and everything goes dull if the camera itself is ever under water.
    internal static class Underwater
    {
        // Sound crossing between air and water: the share of its energy that gets through at the
        // surface is 4 Z1 Z2 / (Z1 + Z2)^2, with the characteristic impedances of air (415 Pa s/m)
        // and water (1.48e6 Pa s/m) (DOSITS): about 0.1%, 29.5 dB. The surface takes every
        // frequency alike, so no low-pass.
        private const float ZAir = 415f, ZWater = 1.48e6f;
        internal static readonly float Gain = Mathf.Sqrt(4f * ZAir * ZWater / ((ZAir + ZWater) * (ZAir + ZWater)));
        internal const float Cutoff = 22000f;
        private const float Fade = 3f; // per second
        private static float s_amount;
        internal static bool ListenerBelow;

        // Nothing is under water well above the sea (lakes and rivers are at sea level too): skips
        // the game's water lookup (a physics overlap) for almost every sound.
        private static bool High(Vector3 p) => ZoneSystem.instance != null && p.y > ZoneSystem.instance.m_waterLevel + 4f;

        internal static bool Below(Vector3 p) => !High(p) && Floating.GetLiquidLevel(p) > p.y + .05f;

        // A sound at p, heard from the listener: muffled only when the whole source is under water.
        // Creature sounds play at their feet or middle, so a swimming greydwarf's would count as under
        // water: for a creature (the sound's own, or the one it plays at) its top has to be under.
        // Anything else: 30 cm under, so splashes at the surface stay clear.
        internal static bool Muffles(Vector3 p, Transform source)
        {
            if (ListenerBelow || High(p) || Floating.GetLiquidLevel(p) <= p.y + .3f) return false;
            Character c = source != null ? source.GetComponentInParent<Character>() : null;
            if (c == null) c = CharacterAt(p);
            if (c == null) return true;
            // the top of its body capsule, else a metre above its feet
            CapsuleCollider body = c.GetComponent<CapsuleCollider>();
            Vector3 top = c.transform.position + Vector3.up;
            if (body != null) { Bounds b = body.bounds; top = new Vector3(b.center.x, b.max.y, b.center.z); }
            return Floating.GetLiquidLevel(top) > top.y + .05f;
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
