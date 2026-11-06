using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Sounds that move with whatever made them. The game spawns many effect sounds at a point and
    // leaves them there (a creature's idle grunts and alert roar, hits, jumps, deaths): a troll's
    // roar stays where it started while the troll walks on. Here a sound spawned for a creature or
    // the player (the effect's parent, or the creature whose idle/alert sound it is) keeps its place
    // relative to it until it ends or its owner is gone. Sounds the game already attaches or follows
    // are left as they are.
    internal static class Follow
    {
        private struct Item { internal Transform Sound, Owner; internal Vector3 Offset; }
        private static readonly List<Item> Items = new List<Item>(32);
        internal static Transform Owner; // set around the game's idle, alert and equip sounds (no parent passed)

        [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
        private static class Created
        {
            private static void Postfix(EffectList __instance, GameObject[] __result, Transform baseParent)
            {
                if (!Plugin.Enabled.Value || __result == null) return;
                if (PlayerSounds.IsCue(__instance)) { PlayerSounds.MarkCue(__result); return; }
                Transform given = baseParent != null ? baseParent : Owner;
                foreach (GameObject go in __result)
                {
                    if (go == null || go.transform.parent != null || go.GetComponent<ZSFX>() == null) continue;
                    // a player's own unplaced sound is placed at the player and follows them
                    ZSFX z = go.GetComponent<ZSFX>();
                    PlayerSounds.Place(z, go.GetComponent<AudioSource>());
                    Transform owner = given != null ? given : go.GetComponent<AcousticFilter>()?.Player;
                    if (owner == null) continue;
                    if (go.GetComponent<UnityEngine.Animations.ParentConstraint>() != null) continue; // the game's own follow
                    Items.Add(new Item { Sound = go.transform, Owner = owner, Offset = owner.InverseTransformPoint(go.transform.position) });
                }
            }
        }

        [HarmonyPatch(typeof(BaseAI), "DoIdleSound")]
        private static class Idle
        {
            private static void Prefix(BaseAI __instance) => Owner = __instance.transform;
            private static void Postfix() => Owner = null;
        }

        [HarmonyPatch(typeof(BaseAI), "SetAlerted")]
        private static class Alerted
        {
            private static void Prefix(BaseAI __instance) => Owner = __instance.transform;
            private static void Postfix() => Owner = null;
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
