using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Sounds that move with whatever made them. The game spawns many effect sounds at a point and
    // leaves them there (a creature's idle grunts and alert roar, hits, jumps, deaths): a troll's
    // roar stays where it started while the troll walks on. Here a sound spawned for a creature (the
    // effect's parent, or the creature whose idle/alert sound it is) keeps its place relative to it
    // until it ends or its owner is gone; its voice (idle and alert calls) comes out of its front
    // (Directions: duller from behind it). The same for a character's own item sounds (equipping,
    // using, picking up, dropping: Equip says whose they are), and for a death: the wail follows the
    // falling body (the ragdoll the game spawns with it). Sounds the game already attaches or follows
    // are left as they are. Only sounds known to be the owner's follow it: a sound made by something
    // else near you (a gate you open, a tree you chop) stays where it was made. (Picking the nearest
    // player within 2.5 m glued those to you, turning with your character.)
    internal static class Follow
    {
        private struct Item { internal Transform Sound, Owner; internal Vector3 Offset; }
        private static readonly List<Item> Items = new List<Item>(32);
        internal static Transform Owner; // set around the game's idle, alert and equip sounds (no parent passed)

        [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
        private static class Created
        {
            private static void Postfix(EffectList __instance, GameObject[] __result, Vector3 basePos, Transform baseParent)
            {
                if (!Plugin.Enabled.Value || __result == null) return;
                if (PlayerSounds.IsCue(__instance)) { PlayerSounds.MarkCue(__result); return; }
                Transform given = baseParent != null ? baseParent : Owner;
                Spots.Place(__instance, __result, basePos, baseParent, ref given);
                // a death: the game spawns the body (a ragdoll) with the death sounds; the wail
                // comes from the falling body, not from where the creature stood
                Transform body = null;
                foreach (GameObject go in __result)
                    if (go != null && go.GetComponent<Ragdoll>() != null)
                    {
                        Rigidbody rb = go.GetComponentInChildren<Rigidbody>();
                        body = rb != null ? rb.transform : go.transform;
                        break;
                    }
                // what each sound was made for is its shape (Area), moving or not
                if (given != null)
                    foreach (GameObject go in __result)
                        if (go != null)
                            foreach (AcousticFilter a in go.GetComponentsInChildren<AcousticFilter>(true)) { a.ShapeRoot = given; a.AreaKnown = false; }
                // a chest opens and closes at its lid, not at its base
                if (given != null && baseParent == null && given.GetComponent<Container>() != null && Area.TopOf(given, out Vector3 lid))
                    foreach (GameObject go in __result)
                        if (go != null && go.transform.parent == null && go.GetComponent<ZSFX>() != null) go.transform.position = lid;
                foreach (GameObject go in __result)
                {
                    if (go == null || go.transform.parent != null || go.GetComponent<ZSFX>() == null) continue;
                    // a player's own unplaced sound is placed at the player and follows them
                    PlayerSounds.Place(go.GetComponent<ZSFX>(), go.GetComponent<AudioSource>());
                    Transform owner = body != null ? body : given != null ? given : go.GetComponent<AcousticFilter>()?.Player;
                    if (owner == null || !Moves(owner)) continue;
                    if (go.GetComponent<UnityEngine.Animations.ParentConstraint>() != null) continue; // the game's own follow
                    Items.Add(new Item { Sound = go.transform, Owner = owner, Offset = owner.InverseTransformPoint(go.transform.position) });
                    // facing: only a creature's voice (its idle and alert calls) comes out of its mouth;
                    // its steps, hits and attacks come from wherever they happen
                    if (Voice && go.GetComponent<AcousticFilter>() is AcousticFilter f) f.Facing = owner;
                }
            }
        }

        [HarmonyPatch(typeof(BaseAI), "DoIdleSound")]
        private static class Idle
        {
            private static void Prefix(BaseAI __instance) { Owner = __instance.transform; Voice = true; }
            private static void Finalizer() { Owner = null; Voice = false; }
        }

        [HarmonyPatch(typeof(BaseAI), "SetAlerted")]
        private static class Alerted
        {
            private static void Prefix(BaseAI __instance) { Owner = __instance.transform; Voice = true; }
            private static void Finalizer() { Owner = null; Voice = false; }
        }

        // A chest's open and close sounds are made at the chest with no owner given, so they had no
        // shape and came from the point at its middle. Around that call the chest is what they're
        // made for: they get its shape (Area) and are heard from its open side. It doesn't move, so
        // nothing follows anything.
        [HarmonyPatch(typeof(Container), nameof(Container.SetInUse))]
        private static class Chest
        {
            private static void Prefix(Container __instance, out bool __state)
            {
                __state = Plugin.Enabled.Value && Owner == null && __instance != null;
                if (__state) Owner = __instance.transform;
            }
            private static void Finalizer(bool __state) { if (__state) Owner = null; }
        }

        private static bool Voice; // inside a creature's idle or alert call

        // Only owners that move (creatures, players, ships, carts): a hit on a tree or a wall stays put
        // without a per-frame follow. Cached per owner.
        private static readonly Dictionary<int, bool> Moving = new Dictionary<int, bool>();
        private static bool Moves(Transform owner)
        {
            int id = owner.GetInstanceID();
            if (Moving.TryGetValue(id, out bool m)) return m;
            if (Moving.Count > 4096) Moving.Clear();
            Rigidbody body = owner.GetComponentInParent<Rigidbody>();
            // creatures and players, ships and carts
            m = owner.GetComponentInParent<Character>() != null || (body != null && !body.isKinematic);
            Moving[id] = m;
            return m;
        }

        // Every frame, from the game's audio update.
        internal static void Step()
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                Item it = Items[i];
                if (it.Sound == null || it.Owner == null || !it.Owner.gameObject.activeInHierarchy)
                {
                    Items.RemoveAt(i);
                    continue;
                }
                it.Sound.position = it.Owner.TransformPoint(it.Offset);
            }
        }

        internal static void Clear() => Items.Clear();
        internal static int Count => Items.Count;
    }
}
