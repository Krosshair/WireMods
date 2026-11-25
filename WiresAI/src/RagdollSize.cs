using System;
using HarmonyLib;
using UnityEngine;

namespace WiresAI
{
    // A creature's body (ragdoll) is as big as the creature was. Valheim spawns every body at one
    // size, whatever the creature's stars, size or age made of it. Creature and body nearly always
    // share the same model, so the size is measured from it: how big its root bone is on one and on
    // the other. It's kept with the body, so everyone sees the same one. Wire's Aldr and Wire's AI
    // both do this; the second one finds the size already right.
    internal static class RagdollSize
    {
        private static readonly int SizeKey = "Wire.RagdollSize".GetStableHashCode();
        private static Character dying;

        [HarmonyPatch(typeof(Character), "OnDeath")]
        private static class Death
        {
            private static void Prefix(Character __instance, out Character __state) { __state = dying; dying = __instance; }
            private static Exception Finalizer(Exception __exception, Character __state) { dying = __state; return __exception; }
        }

        [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Setup))]
        private static class Made
        {
            private static void Postfix(Ragdoll __instance)
            {
                if (dying == null || dying.IsPlayer() || !WiresAIPlugin.On) return;
                ZNetView view = __instance.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || view.GetZDO().GetFloat(SizeKey) > 0f) return;
                float ratio = Ratio(dying, __instance);
                if (!(ratio > 0.01f && ratio < 100f) || Mathf.Abs(ratio - 1f) < 0.01f) return;
                Vector3 size = __instance.transform.localScale * ratio;
                view.GetZDO().Set(SizeKey, size.x);
                __instance.transform.localScale = size;
            }
        }

        // The other players' side: the size comes with the body.
        [HarmonyPatch(typeof(Ragdoll), "Awake")]
        private static class Shown
        {
            private static void Postfix(Ragdoll __instance)
            {
                ZNetView view = __instance.GetComponent<ZNetView>();
                float size = view != null && view.IsValid() ? view.GetZDO().GetFloat(SizeKey) : 0f;
                if (size > 0f) __instance.transform.localScale = Vector3.one * size;
            }
        }

        private static float Ratio(Character creature, Ragdoll body)
        {
            SkinnedMeshRenderer[] models = creature.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer mine in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (mine.sharedMesh == null || mine.rootBone == null) continue;
                foreach (SkinnedMeshRenderer its in models)
                    if (its.sharedMesh == mine.sharedMesh && its.rootBone != null && its.rootBone.name == mine.rootBone.name)
                        return its.rootBone.lossyScale.x / mine.rootBone.lossyScale.x;
            }
            return Grown(creature);
        }

        // No model in common (greydwarves): how much bigger than its prefab the creature is,
        // times the model its stars scale.
        private static float Grown(Character creature)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Utils.GetPrefabName(creature.gameObject)) : null;
            if (prefab == null) return 1f;
            float grown = creature.transform.localScale.x / prefab.transform.localScale.x;
            LevelEffects looks = creature.GetComponentInChildren<LevelEffects>();
            if (looks == null || looks.transform == creature.transform) return grown;
            Transform plain = Utils.FindChild(prefab.transform, looks.name);
            return plain == null ? grown : grown * looks.transform.localScale.x / plain.localScale.x;
        }
    }
}
