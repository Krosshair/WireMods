using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Every sound comes from something with a shape: a hearth, a door, a troll, a cart. Its shape is
    // taken from that thing's colliders (the object the sound is attached to, or the one the game
    // made it for), roughly, as a box: a sound with no such object (an effect left at a point) is a
    // point. From the shape:
    //  - the thing making the sound never hides it (rays to it skip its own colliders);
    //  - its sounds are made where they happen on it (Spots: a chest's lid, a fire's flame, the
    //    middle of a door), and a fire's own sounds are moved to its flame (Voice);
    //  - it sounds as wide as it looks from where you are (Directions: the HRTF's direction is scaled
    //    down by the angle it covers): a hearth across the hall is a point, up close it's in front
    //    of you.
    // Objects bigger than 8 m across (a whole location or dungeon) aren't a sound's shape: those
    // sounds are points.
    internal static class Area
    {
        private struct Shape { internal Vector3 Center, Half; internal bool Valid; }
        private static readonly Dictionary<int, Shape> Shapes = new Dictionary<int, Shape>();
        private const float Biggest = 4f; // half-size

        // Once per sound: what it belongs to and that thing's box (cached per object).
        internal static void Measure(AcousticFilter f, Transform sound)
        {
            if (f.AreaKnown) return;
            f.AreaKnown = true;
            Transform owner = f.ShapeRoot != null ? f.ShapeRoot : (sound.parent != null ? sound.root : null);
            if (owner == null) return;
            int id = owner.GetInstanceID();
            if (!Shapes.TryGetValue(id, out Shape s))
            {
                if (Shapes.Count > 8192) Shapes.Clear();
                s = Of(owner);
                Shapes[id] = s;
            }
            Voice(sound, owner);
            if (!s.Valid) return;
            f.ShapeRoot = owner;
            f.ShapeCenter = s.Center;
            f.ShapeHalf = s.Half;
            f.AreaRadius = Mathf.Max(s.Half.x, s.Half.z);
        }

        // A fire's sounds (a sconce, a hearth, a brazier) are part of the piece and sit wherever the
        // prefab put them, often at its base: moved to the fire itself (its light), on this instance.
        private static void Voice(Transform sound, Transform owner)
        {
            if (sound.parent == null || !sound.IsChildOf(owner) || owner.GetComponent<Fireplace>() == null) return;
            Light best = null;
            float bestD = float.MaxValue;
            foreach (Light l in owner.GetComponentsInChildren<Light>(false))
            {
                if (l == null || !l.enabled) continue;
                float d = (l.transform.position - sound.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = l; }
            }
            if (best != null) sound.position = best.transform.position;
        }

        // The top of a chest's box (world), where its lid opens: where its open and close sounds come
        // from (Follow). False without a usable shape.
        internal static bool TopOf(Transform owner, out Vector3 top)
        {
            top = default;
            if (!Cached(owner, out Shape s)) return false;
            top = owner.TransformPoint(s.Center) + Vector3.up * s.Half.y;
            return true;
        }

        // The middle of a piece's box (world).
        internal static bool CenterOf(Transform owner, out Vector3 at)
        {
            at = default;
            if (!Cached(owner, out Shape s)) return false;
            at = owner.TransformPoint(s.Center);
            return true;
        }

        // A fire's flame: its nearest lit light to the middle of the piece, else the middle.
        internal static bool FlameOf(Transform owner, out Vector3 at)
        {
            bool have = CenterOf(owner, out at);
            Vector3 mid = have ? at : owner.position;
            float best = float.MaxValue;
            foreach (Light l in owner.GetComponentsInChildren<Light>(false))
            {
                if (l == null || !l.enabled) continue;
                float d = (l.transform.position - mid).sqrMagnitude;
                if (d < best) { best = d; at = l.transform.position; have = true; }
            }
            return have;
        }

        private static bool Cached(Transform owner, out Shape s)
        {
            s = default;
            if (owner == null) return false;
            int id = owner.GetInstanceID();
            if (!Shapes.TryGetValue(id, out s))
            {
                if (Shapes.Count > 8192) Shapes.Clear();
                s = Of(owner);
                Shapes[id] = s;
            }
            return s.Valid;
        }

        private static Shape Of(Transform owner)
        {
            Bounds b = default;
            bool have = false;
            // a creature or player: its body (the game's own capsule), not everything it carries
            if (owner.GetComponent<Character>() != null && owner.GetComponent<CapsuleCollider>() is CapsuleCollider body && body.enabled)
            {
                Vector3 h = body.bounds.extents;
                if (h.x > Biggest || h.y > Biggest || h.z > Biggest) return default;
                return new Shape { Center = owner.InverseTransformPoint(body.bounds.center), Half = h, Valid = true };
            }
            foreach (Collider c in owner.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger || !c.enabled) continue;
                if (!have) { b = c.bounds; have = true; }
                else b.Encapsulate(c.bounds);
            }
            if (!have) return default;
            Vector3 half = b.extents;
            if (half.x > Biggest || half.y > Biggest || half.z > Biggest) return default;
            return new Shape { Center = owner.InverseTransformPoint(b.center), Half = half, Valid = true };
        }

        // Share of all around (0 a point, 1 surrounding you) a shape this wide covers from here.
        internal static float WidthAt(float radius, float dist) =>
            radius <= 0f ? 0f : Mathf.Clamp01(2f * Mathf.Atan2(radius, Mathf.Max(.1f, dist)) / Mathf.PI);
    }
}
