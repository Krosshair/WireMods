using System;
using UnityEngine;

namespace WiresBalancedCraftFromNearbyChests
{
    internal static class RefillRouting
    {
        internal static bool Enter(Component component)
        {
            if (component == null)
                return false;

            RouteDefinition route = Resolve(component);
            if (route == null)
                return false;

            return RoutingContext.EnterRefill(
                route,
                component.transform.position);
        }

        internal static RouteDefinition Resolve(
            Component component)
        {
            if (component is Smelter)
                return ResolveSmelter((Smelter)component);

            if (component is Fireplace)
            {
                Fireplace fire = (Fireplace)component;
                return fire.m_canRefill && !fire.m_infiniteFuel
                    ? RouteRegistry.GetRefill("refuel")
                    : null;
            }
            if (component is CookingStation)
                return ResolveCookingStation(
                    (CookingStation)component);

            if (component is Fermenter)
                return RouteRegistry.GetRefill("fermenter");

            if (component is Turret)
                return RouteRegistry.GetRefill("ballista");

            if (component is ShieldGenerator)
                return RouteRegistry.GetRefill(
                    "shieldgenerator");

            if (component is Catapult)
                return RouteRegistry.GetRefill("catapult");

            return null;
        }

        private static RouteDefinition ResolveSmelter(
            Smelter smelter)
        {
            string hint = GetHint(
                smelter,
                smelter.m_name);

            if (Contains(hint, "bathtub") ||
                Contains(hint, "hot tub"))
                return RouteRegistry.GetRefill("refuel");

            if (Contains(hint, "frostfoundry") ||
                Contains(hint, "frost foundry"))
                return RouteRegistry.GetRefill(
                    "frostfoundry");

            if (Contains(hint, "frostkiln") ||
                Contains(hint, "frost kiln") ||
                Contains(hint, "frigid kiln"))
                return RouteRegistry.GetRefill(
                    "frostkiln");

            if (Contains(hint, "charcoal") ||
                Contains(hint, "kiln"))
                return RouteRegistry.GetRefill(
                    "charcoalkiln");

            if (Contains(hint, "blast"))
                return RouteRegistry.GetRefill(
                    "blastfurnace");

            if (Contains(hint, "eitr") ||
                Contains(hint, "refinery"))
                return RouteRegistry.GetRefill(
                    "eitrrefinery");

            if (Contains(hint, "windmill"))
                return RouteRegistry.GetRefill(
                    "windmill");

            if (Contains(hint, "spinning"))
                return RouteRegistry.GetRefill(
                    "spinningwheel");

            return RouteRegistry.GetRefill("smelter");
        }

        private static RouteDefinition ResolveCookingStation(
            CookingStation station)
        {
            string hint = GetHint(
                station,
                station.m_name);

            if (Contains(hint, "oven"))
                return RouteRegistry.GetRefill(
                    "stoneoven");

            return RouteRegistry.GetRefill(
                "cookingstation");
        }

        private static string GetHint(
            Component component,
            string displayName)
        {
            string prefabName =
                Utils.GetPrefabName(component.gameObject);

            return (
                (displayName ?? "") + " " +
                (prefabName ?? ""))
                .ToLowerInvariant();
        }

        private static bool Contains(
            string text,
            string value)
        {
            return text.IndexOf(
                value,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
