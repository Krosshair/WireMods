using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    internal static class RefillTargetRegistry
    {
        private static readonly Dictionary<string, HashSet<Component>>
            ByRoute =
                new Dictionary<string, HashSet<Component>>(
                    StringComparer.Ordinal);

        private static readonly Dictionary<int, string>
            RouteByTarget =
                new Dictionary<int, string>();

        private static float _nextFermenterReconcile;

        internal static void Tick()
        {
            if (Time.time < _nextFermenterReconcile)
                return;

            _nextFermenterReconcile = Time.time + 1f;
            EnsureFermenterTargets();
        }

        internal static void EnsureFermenterTargets()
        {
            Fermenter[] fermenters =
                UnityEngine.Object.FindObjectsByType<Fermenter>(
                    FindObjectsSortMode.None);

            for (int i = 0; i < fermenters.Length; i++)
            {
                Fermenter fermenter = fermenters[i];
                if (fermenter != null)
                    Register(fermenter);
            }
        }

        internal static void Register(Component target)
        {
            if (target == null)
                return;

            RouteDefinition route =
                RefillRouting.Resolve(target);

            if (route == null)
                return;

            int id = target.GetInstanceID();
            string oldRoute;
            if (RouteByTarget.TryGetValue(id, out oldRoute))
            {
                if (oldRoute == route.Id)
                    return;

                RemoveFromRoute(target, oldRoute);
            }

            HashSet<Component> set;
            if (!ByRoute.TryGetValue(route.Id, out set))
            {
                set = new HashSet<Component>();
                ByRoute.Add(route.Id, set);
            }

            set.Add(target);
            RouteByTarget[id] = route.Id;
            ConnectionVisuals.NotifyGraphChanged();
        }

        internal static void Unregister(Component target)
        {
            if (target == null)
                return;

            int id = target.GetInstanceID();
            string routeId;
            if (!RouteByTarget.TryGetValue(id, out routeId))
                return;

            RemoveFromRoute(target, routeId);
            RouteByTarget.Remove(id);
            ConnectionVisuals.NotifyGraphChanged();
        }

        private static void RemoveFromRoute(
            Component target,
            string routeId)
        {
            HashSet<Component> set;
            if (routeId != null &&
                ByRoute.TryGetValue(routeId, out set))
            {
                set.Remove(target);
                if (set.Count == 0)
                    ByRoute.Remove(routeId);
            }
        }

        internal static IEnumerable<Component> GetTargets(
            RouteDefinition route)
        {
            if (route == null)
                yield break;

            HashSet<Component> set;
            if (!ByRoute.TryGetValue(route.Id, out set))
                yield break;

            List<Component> dead = null;
            foreach (Component target in set)
            {
                if (target == null)
                {
                    if (dead == null)
                        dead = new List<Component>();
                    dead.Add(target);
                    continue;
                }

                yield return target;
            }

            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                    set.Remove(dead[i]);
            }
        }

        internal static Component FindHoveredTarget(
            GameObject hover)
        {
            if (hover == null)
                return null;

            // Fermenters are a little odd in their prefab hierarchy, so
            // resolve them directly before the generic Piece traversal.
            Fermenter directFermenter =
                hover.GetComponentInParent<Fermenter>();

            if (directFermenter != null)
            {
                Register(directFermenter);
                return directFermenter;
            }

            Piece piece = hover.GetComponentInParent<Piece>();
            if (piece != null)
            {
                Component child = FindInChildren(piece.gameObject);
                if (child != null)
                    return child;
            }

            Component direct = FindOnParents(hover);
            if (direct != null)
                return direct;

            ZNetView view = hover.GetComponentInParent<ZNetView>();
            return view != null
                ? FindInChildren(view.gameObject)
                : null;
        }

        private static Component FindOnParents(GameObject go)
        {
            Component c = go.GetComponentInParent<Smelter>();
            if (c != null) return c;

            c = go.GetComponentInParent<CookingStation>();
            if (c != null && RefillRouting.Resolve(c) != null) return c;

            c = go.GetComponentInParent<Fermenter>();
            if (c != null) return c;

            c = go.GetComponentInParent<Turret>();
            if (c != null) return c;

            c = go.GetComponentInParent<ShieldGenerator>();
            if (c != null) return c;

            c = go.GetComponentInParent<Catapult>();
            if (c != null) return c;

            return go.GetComponentInParent<Fireplace>();
        }
        private static Component FindInChildren(GameObject root)
        {
            Component c = root.GetComponentInChildren<Smelter>(true);
            if (c != null) return c;

            CookingStation cooking =
                root.GetComponentInChildren<CookingStation>(true);
            if (cooking != null &&
                RefillRouting.Resolve(cooking) != null)
                return cooking;

            c = root.GetComponentInChildren<Fermenter>(true);
            if (c != null) return c;

            c = root.GetComponentInChildren<Turret>(true);
            if (c != null) return c;

            c = root.GetComponentInChildren<ShieldGenerator>(true);
            if (c != null) return c;

            c = root.GetComponentInChildren<Catapult>(true);
            if (c != null) return c;

            return root.GetComponentInChildren<Fireplace>(true);
        }

        internal static void Clear()
        {
            ByRoute.Clear();
            RouteByTarget.Clear();
            _nextFermenterReconcile = 0f;
        }
    }

    [HarmonyPatch(typeof(Smelter), "Awake")]
    internal static class RegisterSmelterTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Smelter __instance)
        {
            RefillTargetRegistry.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(Fireplace), "Awake")]
    internal static class RegisterFireplaceTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Fireplace __instance)
        {
            RefillTargetRegistry.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(CookingStation), "Awake")]
    internal static class RegisterCookingTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(CookingStation __instance)
        {
            RefillTargetRegistry.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(Fermenter), "Awake")]
    internal static class RegisterFermenterTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Fermenter __instance)
        {
            RefillTargetRegistry.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(Turret), "Awake")]
    internal static class RegisterTurretTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Turret __instance)
        {
            RefillTargetRegistry.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(ShieldGenerator), "Start")]
    internal static class RegisterShieldTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ShieldGenerator __instance)
        {
            RefillTargetRegistry.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(Catapult), "Start")]
    internal static class RegisterCatapultTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Catapult __instance)
        {
            RefillTargetRegistry.Register(__instance);
        }
    }
}
