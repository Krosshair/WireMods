using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Every sound comes from something with a shape: a hearth, a door, a troll, a cart. Its shape is
    // taken from that thing's colliders (the object the sound is attached to, or the one the game
    // made it for), roughly, as a box: a sound with no such object (an effect left at a point) is a
    // point. From the shape:
    //  - the thing making the sound never hides it (rays to it skip its own colliders);
    //  - it's hidden only as much as its least hidden part: the sound's own spot and the shape's
    //    middle, both sides as seen from you and its top are all checked (Batch), so anything
    //    poking out past a wall, a doorframe or another piece is heard;
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
            if (!s.Valid) return;
            f.ShapeRoot = owner;
            f.ShapeCenter = s.Center;
            f.ShapeHalf = s.Half;
            f.AreaRadius = Mathf.Max(s.Half.x, s.Half.z);
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

        // The points to check for an area sound (besides its own spot): the middle, both sides as
        // seen from the listener, the top. Slightly inside the box, so a point isn't on a neighbour's face.
        internal static int Points(AcousticFilter f, Vector3 listener, Vector3[] into)
        {
            Transform owner = f.ShapeRoot;
            if (owner == null || f.AreaRadius <= 0f) return 0;
            Vector3 c = owner.TransformPoint(f.ShapeCenter);
            Vector3 across = Vector3.Cross(Vector3.up, c - listener);
            across = across.sqrMagnitude > 1e-4f ? across.normalized : Vector3.right;
            float side = Mathf.Max(f.ShapeHalf.x, f.ShapeHalf.z) * .85f;
            into[0] = Heard(f, listener);
            into[1] = c + across * side;
            into[2] = c - across * side;
            into[3] = c + Vector3.up * f.ShapeHalf.y * .85f;
            return 4;
        }

        // Where a sound with a shape is heard from: the side of it facing you, drawn a quarter of the
        // way in toward its middle. A chest set into a wall is heard from its front, where you see
        // it, not from its middle inside the wall; a hearth from its near side, not its far one.
        // Inside the shape: its middle.
        internal static Vector3 Heard(AcousticFilter f, Vector3 listener)
        {
            Transform owner = f.ShapeRoot;
            Vector3 c = owner.TransformPoint(f.ShapeCenter), h = f.ShapeHalf;
            Vector3 near = new Vector3(Mathf.Clamp(listener.x, c.x - h.x, c.x + h.x),
                                       Mathf.Clamp(listener.y, c.y - h.y, c.y + h.y),
                                       Mathf.Clamp(listener.z, c.z - h.z, c.z + h.z));
            return near == listener ? c : Vector3.Lerp(near, c, .25f);
        }

        // Share of all around (0 a point, 1 surrounding you) a shape this wide covers from here.
        internal static float WidthAt(float radius, float dist) =>
            radius <= 0f ? 0f : Mathf.Clamp01(2f * Mathf.Atan2(radius, Mathf.Max(.1f, dist)) / Mathf.PI);
    }
}
