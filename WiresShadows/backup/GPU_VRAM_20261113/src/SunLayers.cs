using HarmonyLib;
using UnityEngine;

namespace WiresShadows
{
    // Small things stop casting sun and moon shadows past a distance: items on the ground and
    // pickables (layer "item") past 30 m, bushes, berry bushes, small saplings and ore bits
    // ("Default_small", checked in the game's prefabs) past 40 m. Out there their shadows are a few
    // pixels of the far cascades, but each one is still a draw call. Unity does the cut itself
    // (Light.layerShadowCullDistances). Set once when the sun is made and when the setting
    // changes; nothing per frame.
    // The distances are judgement values (no source). Trees, rocks and buildings are left alone.
    internal static class SunLayers
    {
        private const float ItemDistance = 30f, SmallDistance = 40f;
        private static readonly int Item = LayerMask.NameToLayer("item");
        private static readonly int Small = LayerMask.NameToLayer("Default_small");

        internal static void Apply()
        {
            Light sun = EnvMan.instance != null ? EnvMan.instance.m_dirLight : null;
            if (sun == null) return;
            bool on = Plugin.UseShader.Value && Plugin.SunSmallCull.Value;
            float[] d = new float[32]; // 0: the normal shadow distance
            if (on)
            {
                if (Item >= 0) d[Item] = ItemDistance;
                if (Small >= 0) d[Small] = SmallDistance;
            }
            sun.layerShadowCullDistances = d;
            Plugin.Note("[sun] small things' sun shadows " + (on ? "cut at " + ItemDistance + " m (items), " + SmallDistance + " m (bushes)" : "as the game has them"));
        }

        [HarmonyPatch(typeof(EnvMan), "Awake")]
        private static class OnEnvMan
        {
            private static void Postfix() => Apply();
        }
    }
}
