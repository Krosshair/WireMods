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

        // Rebuild a shape only when the underlying geometry changes,
        // rather than polling or recomputing collider bounds every frame.
        internal static void Invalidate(Transform owner)
        {
            if (owner == null) return;
            // Collider geometry can belong to a nested child while the
            // sound caches the shape of its parent or root. Invalidate all
            // cached ancestor boxes, not just this one Transform's ID.
            for (Transform t = owner; t != null; t = t.parent)
                Shapes.Remove(t.GetInstanceID());
            foreach (AcousticFilter f in AcousticFilter.Live)
                if (f != null && f.ShapeRoot != null &&
                    (f.ShapeRoot == owner || f.ShapeRoot.IsChildOf(owner) ||
                     owner.IsChildOf(f.ShapeRoot)))
                {
                    f.AreaKnown = false;
                    Shapes.Remove(f.ShapeRoot.GetInstanceID());
                }
        }

        internal static void InvalidateAll()
        {
            Shapes.Clear();
            foreach (AcousticFilter f in AcousticFilter.Live)
                if (f != null) f.AreaKnown = false;
        }

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
            // The cached half-size is in the owner's LOCAL coordinates.
            // Derive acoustic dimensions in metres using actual world scale.
            Vector3 scale = owner.lossyScale;
            Vector3 size = new Vector3(s.Half.x * Mathf.Abs(scale.x),
                s.Half.y * Mathf.Abs(scale.y), s.Half.z * Mathf.Abs(scale.z));
            // Preserve the game's authored relative levels for every flame.
            // Additional size-derived gain (-14 to -18 dB on sconces)
            // buried otherwise independent sources beneath larger fires.
            // Source size still controls its physical emission area and HRTF
            // width; native AudioSource volume and distance handle level.
            f.ShapeRoot = owner;
            f.ShapeCenter = s.Center;
            f.ShapeHalf = s.Half;
            f.AreaRadius = Mathf.Max(size.x, size.z);
        }

        // A piece's own sounds sit wherever the prefab put them. A fire's (a sconce, a hearth, a
        // brazier) often at its base: moved to the fire itself (its light). Any other piece's sound
        // left at the piece's pivot (not placed by the prefab at all): moved to the middle of the
        // piece. On this instance, once. Characters' own sounds are placed elsewhere (Spots).
        private static void Voice(Transform sound, Transform owner)
        {
            if (sound.parent == null || !sound.IsChildOf(owner) || owner.GetComponent<Character>() != null) return;
            if (owner.GetComponent<Fireplace>() == null)
            {
                if ((sound.position - owner.position).sqrMagnitude < 1e-4f && CenterOf(owner, out Vector3 mid)) sound.position = mid;
                return;
            }
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
            top = owner.TransformPoint(s.Center + Vector3.up * s.Half.y);
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
            Bounds local = default;
            bool have = false;
            // Characters are represented by the body capsule, not the
            // weapons and carried equipment included in their hierarchy.
            if (owner.GetComponent<Character>() != null &&
                owner.GetComponent<CapsuleCollider>() is CapsuleCollider body &&
                body.enabled && !body.isTrigger)
                AddCollider(owner, body, ref local, ref have);
            else
                foreach (Collider c in owner.GetComponentsInChildren<Collider>())
                {
                    if (c == null || c.isTrigger || !c.enabled) continue;
                    AddCollider(owner, c, ref local, ref have);
                }
            if (!have) return default;
            Vector3 half = local.extents;
            Vector3 scale = owner.lossyScale;
            if (half.x * Mathf.Abs(scale.x) > Biggest ||
                half.y * Mathf.Abs(scale.y) > Biggest ||
                half.z * Mathf.Abs(scale.z) > Biggest) return default;
            return new Shape { Center = local.center, Half = half, Valid = true };
        }

        // Include actual oriented collider bounds in the owner's local
        // coordinates. A prefab rotated in the world must not turn its
        // surface box into a larger world-aligned box or mix coordinate
        // spaces when deriving sound origins.
        private static void AddCollider(Transform owner, Collider c,
            ref Bounds combined, ref bool have)
        {
            Bounds box;
            Transform ct = c.transform;
            if (c is BoxCollider bc)
                box = new Bounds(bc.center, bc.size);
            else if (c is SphereCollider sc)
                box = new Bounds(sc.center, Vector3.one * (2f * sc.radius));
            else if (c is CapsuleCollider cc)
            {
                float r = cc.radius;
                Vector3 h = Vector3.one * (r * 2f);
                h[cc.direction] = Mathf.Max(cc.height, r * 2f);
                box = new Bounds(cc.center, h);
            }
            else if (c is MeshCollider mc && mc.sharedMesh != null)
                box = mc.sharedMesh.bounds;
            else
            {
                // Unknown collider: its world AABB is conservative.
                // Still transform all corners into the SAME local space.
                Bounds world = c.bounds;
                AddCorners(owner, null, world, ref combined, ref have);
                return;
            }
            AddCorners(owner, ct, box, ref combined, ref have);
        }

        private static void AddCorners(Transform owner, Transform collider,
            Bounds bounds, ref Bounds combined, ref bool have)
        {
            Vector3 c = bounds.center, h = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 p = c + new Vector3(h.x * x, h.y * y, h.z * z);
                        Vector3 world = collider != null ? collider.TransformPoint(p) : p;
                        p = owner.InverseTransformPoint(world);
                        if (have) combined.Encapsulate(p);
                        else { combined = new Bounds(p, Vector3.zero); have = true; }
                    }
        }

        // Share of all around (0 a point, 1 surrounding you) a shape this wide covers from here.
        internal static float WidthAt(float radius, float dist) =>
            radius <= 0f ? 0f : Mathf.Clamp01(2f * Mathf.Atan2(radius, Mathf.Max(.1f, dist)) / Mathf.PI);
    }
}
