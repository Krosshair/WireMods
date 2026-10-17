using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Restrict bonus copies to naturally harvested/destructible world
    // objects. DropTable.GetDropList also powers unrelated gameplay,
    // including many non-resource objects, so item name alone is not
    // sufficient evidence that a drop is a natural material harvest.
    [HarmonyPatch]
    internal static class NaturalDropSourceScope
    {
        [ThreadStatic] private static int _depth;

        internal static bool IsNaturalSource
        {
            get { return _depth > 0; }
        }

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase method;
            method = AccessTools.Method(typeof(MineRock5), "DamageArea");
            if (method != null) yield return method;

            method = AccessTools.Method(typeof(MineRock), "RPC_Hit");
            if (method != null) yield return method;

            method = AccessTools.Method(typeof(TreeBase), "RPC_Damage");
            if (method != null) yield return method;

            method = AccessTools.Method(typeof(TreeLog), "Destroy");
            if (method != null) yield return method;

            method = AccessTools.Method(typeof(DropOnDestroyed), "OnDestroyed");
            if (method != null) yield return method;
        }

        [HarmonyPrefix]
        private static void Prefix(object __instance, out bool __state)
        {
            __state = IsNaturalObject(__instance);
            if (__state)
                _depth++;
        }

        // Finalizer runs even when a destroyed object's method throws,
        // preventing stale state from affecting later unrelated drops.
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state && _depth > 0)
                _depth--;
            return __exception;
        }

        private static bool IsNaturalObject(object value)
        {
            if (value is MineRock5 || value is MineRock ||
                value is TreeBase || value is TreeLog)
                return true;

            DropOnDestroyed destroyed = value as DropOnDestroyed;
            if (destroyed == null)
                return false;

            GameObject source = destroyed.gameObject;
            if (source == null ||
                source.GetComponent<Piece>() != null ||
                source.GetComponent<Character>() != null ||
                source.GetComponent<Container>() != null)
                return false;

            string n = source.name.ToLowerInvariant();
            // General natural rock/tree/mineral sources only. Other
            // DropOnDestroyed users cannot get extra wood/ore merely
            // because an unrelated prefab drops one of those items.
            string[] prefixes =
            {
                "rock", "stone", "tree", "log", "mine", "ore",
                "boulder", "copper", "tin", "silver", "obsidian",
                "marble", "tar", "swamptree", "ancienttree",
                "yggdrasil", "grausten", "flametal", "petrified",
                "ashlandtree", "deeptree"
            };
            foreach (string prefix in prefixes)
                if (n.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
