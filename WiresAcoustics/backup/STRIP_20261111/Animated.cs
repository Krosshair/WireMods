using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Sounds an animation plays (AnimationEffect.Effect: a crossbow's or grappling hook's reload, many
    // creature attack and movement sounds) are spawned at the event's joint and left in the world, so
    // they stayed where the reload started while you walked on. Same method as the game's, except that
    // when the animated thing can move (a character, a ship, a cart), the sound parts are attached to
    // that joint. A sound object that carries particles too is left unattached (its particles would be
    // dragged along).
    [HarmonyPatch(typeof(AnimationEffect), nameof(AnimationEffect.Effect))]
    internal static class Animated
    {
        private static bool Prefix(AnimationEffect __instance, AnimationEvent e)
        {
            if (!Plugin.Enabled.Value || __instance == null || e == null) return true;
            GameObject prefab = e.objectReferenceParameter as GameObject;
            if (prefab == null) return false;
            Transform joint = null;
            string name = e.stringParameter;
            if (!string.IsNullOrEmpty(name)) joint = Utils.FindChild(__instance.transform, name);
            if (joint == null) joint = __instance.m_effectRoot ? __instance.m_effectRoot : __instance.transform;
            GameObject go = Object.Instantiate(prefab, joint.position, joint.rotation);
            if (!Follow.Moves(__instance.transform)) return false;
            foreach (ZSFX z in go.GetComponentsInChildren<ZSFX>(true))
                if (z.GetComponent("ParticleSystem") == null) z.transform.SetParent(joint, true);
            return false;
        }
    }
}
