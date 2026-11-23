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
        // Equipped weapon models often have no physics colliders. Cache their
        // *visible* mesh bounds separately from collider-based world shapes.
        private static readonly Dictionary<int, Shape> WeaponShapes = new Dictionary<int, Shape>();
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
            {
                Shapes.Remove(t.GetInstanceID());
                WeaponShapes.Remove(t.GetInstanceID());
            }
            foreach (AcousticFilter f in AcousticFilter.Live)
                if (f != null && f.ShapeRoot != null &&
                    (f.ShapeRoot == owner || f.ShapeRoot.IsChildOf(owner) ||
                     owner.IsChildOf(f.ShapeRoot)))
                {
                    f.AreaKnown = false;
                    Shapes.Remove(f.ShapeRoot.GetInstanceID());
                    WeaponShapes.Remove(f.ShapeRoot.GetInstanceID());
                }
        }

        internal static void InvalidateAll()
        {
            Shapes.Clear();
            WeaponShapes.Clear();
            foreach (AcousticFilter f in AcousticFilter.Live)
                if (f != null) f.AreaKnown = false;
        }

        // Resolve a source from its nearest meaningful ancestor. A global
        // sound.root can be a whole scene, ship or other aggregate while the
        // emitter belongs to one smaller networked/physical object in it.
        // Explicit event ownership (ShapeRoot) still takes precedence.
        private static Transform NearestOwner(Transform sound)
        {
            if (sound == null || sound.parent == null) return null;
            for (Transform t = sound.parent; t != null; t = t.parent)
                if (t.GetComponent<ZNetView>() != null ||
                    t.GetComponent<Character>() != null ||
                    t.GetComponent<Piece>() != null ||
                    t.GetComponent<Container>() != null ||
                    t.GetComponent<Fireplace>() != null ||
                    t.GetComponent<Destructible>() != null ||
                    t.GetComponent<Rigidbody>() != null)
                    return t;
            return sound.root;
        }

        // A physical object radiates from its exposed exterior. Only a
        // specifically localized action, the fire's flame or a swept weapon
        // keeps its separate emission domain. This is a general rule across
        // all eligible objects, not a list of chest/machine prefab names.
        internal static bool SurfaceDomain(bool owner, bool localized,
                                           bool fire, bool weapon) =>
            owner && !localized && !fire && !weapon;

        // Once per sound: what it belongs to and that thing's box (cached per object).
        internal static void Measure(AcousticFilter f, Transform sound)
        {
            // Re-apply after a mod toggle (ReleaseAll resets SourceLevel),
            // without rescanning geometry or reading collider bounds.
            if (f.AreaKnown)
            {
                if (f.FireLoop) f.SourceLevel = FireLevelFromRange(f.AuthoredMaxDistance);
                return;
            }
            f.AreaKnown = true;
            Transform owner = f.ShapeRoot != null ? f.ShapeRoot : NearestOwner(sound);
            bool fire = owner != null && owner.GetComponent<Fireplace>() != null &&
                        sound.IsChildOf(owner) && f.Source != null && f.Source.loop;
            if (f.FireLoop && !fire) f.SourceLevel = 1f;
            f.FireLoop = fire;
            if (fire) f.SourceLevel = FireLevelFromRange(f.AuthoredMaxDistance);

            // A reused sound can change owner, or its original owner can lose
            // its colliders. Never let an earlier measured box silently leak
            // into the new playback's occlusion and HRTF calculations.
            f.ShapeCenter = Vector3.zero;
            f.ShapeHalf = Vector3.zero;
            f.AreaRadius = 0f;
            // Distributed sounds from physical objects radiate from their
            // exposed surfaces, not random points *inside* their solid box.
            // Genuine point events (hits, hinges, mouths, footsteps) remain
            // at their authored action origin. Fire and swung weapon audio
            // use their existing flame/visible-mesh models instead.
            f.SurfaceEmitter = SurfaceDomain(owner != null,
                                             f.LocalizedEvent, fire,
                                             f.WeaponSource);
            if (owner == null) return;
            Shape s;
            if (!(f.WeaponSource ? WeaponCached(owner, out s) : Cached(owner, out s)))
                return;
            // A weapon sweep has already been positioned at the visible
            // weapon's midpoint; generic prefab recentering must not move it.
            if (!f.WeaponSource) Voice(sound, owner);
            // The cached half-size is in the owner's LOCAL coordinates.
            // Derive acoustic dimensions in metres using actual world scale.
            Vector3 scale = owner.lossyScale;
            Vector3 size = new Vector3(s.Half.x * Mathf.Abs(scale.x),
                s.Half.y * Mathf.Abs(scale.y), s.Half.z * Mathf.Abs(scale.z));
            // Shape affects the apparent source width and origin, never its
            // acoustic power. Power comes from Valheim's authored sound range.
            f.ShapeRoot = owner;
            f.ShapeCenter = s.Center;
            f.ShapeHalf = s.Half;
            f.AreaRadius = f.WeaponSource
                ? Mathf.Max(size.x, Mathf.Max(size.y, size.z))
                : Mathf.Max(size.x, size.z);
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

        // A moving swing comes from the visible blade/head, not the palm.
        // Weapon models commonly lack physics colliders. Derive the rotated
        // box from actual mesh bounds, cache it per held visual instance,
        // and reuse it for all swings from this equipped item.
        internal static bool WeaponCenterOf(Transform weapon, out Vector3 center)
        {
            center = default;
            if (!WeaponCached(weapon, out Shape shape)) return false;
            center = weapon.TransformPoint(shape.Center);
            return true;
        }

        private static bool WeaponCached(Transform weapon, out Shape shape)
        {
            shape = default;
            if (weapon == null) return false;
            int id = weapon.GetInstanceID();
            if (!WeaponShapes.TryGetValue(id, out shape))
            {
                if (WeaponShapes.Count > 4096) WeaponShapes.Clear();
                shape = WeaponOf(weapon);
                WeaponShapes[id] = shape;
            }
            return shape.Valid;
        }

        private static Shape WeaponOf(Transform weapon)
        {
            Bounds local = default;
            bool have = false;
            foreach (MeshFilter mesh in weapon.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mesh == null || mesh.sharedMesh == null) continue;
                MeshRenderer visible = mesh.GetComponent<MeshRenderer>();
                if (visible == null || !visible.enabled) continue;
                AddCorners(weapon, mesh.transform, mesh.sharedMesh.bounds,
                    ref local, ref have);
            }
            foreach (SkinnedMeshRenderer skin in weapon.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (skin == null || !skin.enabled || skin.sharedMesh == null) continue;
                AddCorners(weapon, skin.transform, skin.localBounds,
                    ref local, ref have);
            }
            if (!have) return Of(weapon); // a collider-only weapon model
            Vector3 half = local.extents, scale = weapon.lossyScale;
            if (half.x * Mathf.Abs(scale.x) > Biggest ||
                half.y * Mathf.Abs(scale.y) > Biggest ||
                half.z * Mathf.Abs(scale.z) > Biggest) return default;
            return new Shape { Center = local.center, Half = half, Valid = true };
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

        // The game's authored AudioSource.maxDistance is a design range, not
        // an absolute SPL measurement. Relative source amplitude is scaled
        // linearly with that range (inverse-distance law); at the same
        // listener distance, a 3 m fire is 20log10(3/10) = -10.46 dB
        // relative to a 10 m full-size reference. No collider-size guessing,
        // no arbitrary additional -18 dB clamp, no per-clip voice selection.
        // >=10 m stays at the game's own level, not artificially boosted.
        // Invalid ranges preserve authored volume, rather than mute a source.
        private const float ReferenceFireRange = 10f;
        private static float FireLevelFromRange(float authoredRange) =>
            authoredRange > 0f && !float.IsNaN(authoredRange) ?
                Mathf.Min(1f, authoredRange / ReferenceFireRange) : 1f;

        // Share of all around (0 a point, 1 surrounding you) a shape this wide covers from here.
        internal static float WidthAt(float radius, float dist) =>
            radius <= 0f ? 0f : Mathf.Clamp01(2f * Mathf.Atan2(radius, Mathf.Max(.1f, dist)) / Mathf.PI);
    }
}
