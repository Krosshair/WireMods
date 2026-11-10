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
            f.HeardKnown = false;
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
            if (owner == null) return false;
            int id = owner.GetInstanceID();
            if (!Shapes.TryGetValue(id, out Shape s))
            {
                if (Shapes.Count > 8192) Shapes.Clear();
                s = Of(owner);
                Shapes[id] = s;
            }
            if (!s.Valid) return false;
            top = owner.TransformPoint(s.Center) + Vector3.up * s.Half.y;
            return true;
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
            // all on the side facing you: the nearest point of its surface, either side of it and its top
            Vector3 near = Heard(f, listener);
            into[0] = near;
            into[1] = Surface(f, c, near + across * side, listener);
            into[2] = Surface(f, c, near - across * side, listener);
            into[3] = Surface(f, c, new Vector3(near.x, c.y + f.ShapeHalf.y, near.z), listener);
            return 4;
        }

        // The nearest point of a shape's surface, just outside it (a chest set into a wall: its front,
        // where you see it). Inside the shape: its middle. One of the points Batch tries when the
        // sound's own spot is blocked; a sound whose own spot is in the clear is heard from there.
        internal static Vector3 Heard(AcousticFilter f, Vector3 listener)
        {
            Vector3 c = f.ShapeRoot.TransformPoint(f.ShapeCenter);
            return Surface(f, c, listener, listener);
        }

        // A point clamped onto the shape's box, then 5 cm out toward you (so a check toward it isn't
        // stopped by the thing it sits on); the middle if you're inside the box.
        private static Vector3 Surface(AcousticFilter f, Vector3 c, Vector3 p, Vector3 listener)
        {
            Vector3 h = f.ShapeHalf;
            Vector3 q = new Vector3(Mathf.Clamp(p.x, c.x - h.x, c.x + h.x),
                                    Mathf.Clamp(p.y, c.y - h.y, c.y + h.y),
                                    Mathf.Clamp(p.z, c.z - h.z, c.z + h.z));
            Vector3 out_ = listener - q;
            if (out_.sqrMagnitude < 1e-6f) return c;
            return q + out_.normalized * .05f;
        }

        // Share of all around (0 a point, 1 surrounding you) a shape this wide covers from here.
        internal static float WidthAt(float radius, float dist) =>
            radius <= 0f ? 0f : Mathf.Clamp01(2f * Mathf.Atan2(radius, Mathf.Max(.1f, dist)) / Mathf.PI);
    }
}
