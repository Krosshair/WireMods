using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WiresCombatFlow
{
    /// <summary>
    /// Changes only the direction supplied to vanilla's actual melee hit casts.
    /// No camera changes, animator changes, extra reach or per-frame searches.
    /// </summary>
    internal static class AutoVerticalAim
    {
        private const int MaxColliders = 48;

        private static readonly Collider[] Nearby =
            new Collider[MaxColliders];

        private static readonly int CharacterMask =
            LayerMask.GetMask(
                "character",
                "character_net",
                "character_ghost",
                "hitbox",
                "character_noenv",
                "vehicle");

        private static readonly int WorldMask =
            LayerMask.GetMask(
                "Default",
                "Default_small",
                "static_solid",
                "piece",
                "terrain",
                "blocker");

        private static readonly AccessTools.FieldRef<Attack, Humanoid>
            Attacker =
                AccessTools.FieldRefAccess<Attack, Humanoid>(
                    "m_character");

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> MaxCorrection;
        internal static ConfigEntry<float> HorizontalCone;

        [ThreadStatic]
        private static Attack _evaluatingHit;

        internal static void Bind(ConfigFile file)
        {
            Enabled = file.Bind(
                "Vertical Melee Aim",
                "Enabled",
                true,
                "Automatically correct only the invisible vertical hit direction toward an enemy in the existing melee attack arc.");

            MaxCorrection = file.Bind(
                "Vertical Melee Aim",
                "Maximum Correction Angle",
                65f,
                new ConfigDescription(
                    "Maximum absolute up/down hit angle in degrees. No extra reach or wider horizontal attack arc.",
                    new AcceptableValueRange<float>(0f, 85f)));

            HorizontalCone = file.Bind(
                "Vertical Melee Aim",
                "Search Cone Half Angle",
                25f,
                new ConfigDescription(
                    "Maximum sideways angle for identifying a target. It never exceeds the weapon's normal horizontal attack arc.",
                    new AcceptableValueRange<float>(1f, 45f)));
        }

        internal static void BeginHit(Attack attack)
        {
            _evaluatingHit = attack;
        }

        internal static void EndHit()
        {
            _evaluatingHit = null;
        }

        internal static void CorrectDirection(
            Attack attack,
            Transform originJoint,
            ref Vector3 direction)
        {
            if (_evaluatingHit != attack ||
                Enabled == null ||
                !Enabled.Value ||
                MaxCorrection.Value <= 0f ||
                originJoint == null ||
                attack == null ||
                (attack.m_attackType != Attack.AttackType.Horizontal &&
                 attack.m_attackType != Attack.AttackType.Vertical) ||
                attack.m_isHomeItem ||
                attack.m_harvest ||
                attack.m_pickaxeSpecial)
            {
                return;
            }

            Humanoid humanoid = Attacker(attack);
            Player player = humanoid as Player;

            if (player == null ||
                player != Player.m_localPlayer ||
                player.IsDead() ||
                player.InPlaceMode())
            {
                return;
            }

            ItemDrop.ItemData weapon = attack.GetWeapon();
            if (weapon == null ||
                weapon.m_shared.m_tamedOnly)
            {
                return;
            }

            Transform facing = player.transform;

            Vector3 forward = facing.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                return;

            forward.Normalize();

            // Identical physical origin to Attack.DoMeleeAttack's casts.
            Vector3 origin =
                originJoint.position +
                Vector3.up * attack.m_attackHeight +
                facing.right * attack.m_attackOffset;

            float reach = Mathf.Max(0f, attack.m_attackRange);

            if (reach <= 0.05f)
                return;

            // Matches Attack's protected static character-mask
            // initialization, including the vehicle layer.
            int characterMask = CharacterMask;

            float castRadius = Mathf.Max(
                attack.m_attackRayWidth,
                attack.m_attackRayWidth +
                attack.m_attackRayWidthCharExtra);
            castRadius = Mathf.Max(0f, castRadius);

            float queryRadius = reach + castRadius +
                Mathf.Max(
                    Mathf.Abs(attack.m_attackHeightChar1),
                    Mathf.Abs(attack.m_attackHeightChar2));

            // Vertical attacks have no horizontal fan. Account for
            // the actual enemy collider width instead of demanding
            // its *center* be within an arbitrary 6-degree cone.
            // This changes target selection only, never swing yaw.
            float halfCone =
                attack.m_attackType == Attack.AttackType.Vertical
                    ? 0f
                    : Mathf.Min(
                        HorizontalCone.Value,
                        Mathf.Max(0f, attack.m_attackAngle * .5f));

            // One fixed-size broadphase query on attack frames only.
            int count =
                Physics.OverlapSphereNonAlloc(
                    origin,
                    queryRadius,
                    Nearby,
                    characterMask,
                    QueryTriggerInteraction.Ignore);

            if (count <= 0 ||
                count >= Nearby.Length)
            {
                return;
            }

            float bestScore = float.MaxValue;
            Vector3 bestPoint = Vector3.zero;
            Vector3 bestVisibilityPoint = Vector3.zero;
            Vector3 bestUpperVisibilityPoint = Vector3.zero;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Collider collider = Nearby[i];
                if (collider == null ||
                    !collider.enabled)
                {
                    continue;
                }

                GameObject hitObject =
                    Projectile.FindHitObject(collider);

                Character target =
                    hitObject != null
                        ? hitObject.GetComponent<Character>()
                        : null;

                if (target == null)
                    target =
                        collider.GetComponentInParent<Character>();

                if (target == null ||
                    target == player ||
                    target.IsDead())
                {
                    continue;
                }

                BaseAI ai = target.GetBaseAI();
                if (!BaseAI.IsEnemy(player, target) &&
                    (ai == null || !ai.IsAggravatable()))
                {
                    continue;
                }

                Bounds bounds = collider.bounds;
                Vector3 center = bounds.center;
                Vector3 flat = center - origin;
                flat.y = 0f;

                float flatSqr = flat.sqrMagnitude;
                if (flatSqr < 0.0004f)
                    continue;

                float forwardDistance =
                    Vector3.Dot(flat, forward);

                if (forwardDistance <= 0.01f)
                    continue;

                float lateral =
                    Mathf.Abs(Vector3.Dot(flat, facing.right));

                // A wide creature can genuinely intersect the melee
                // sweep even with its *center* outside the attack arc.
                // A small target must still be nearly in front.
                float lateralRadius =
                    Mathf.Abs(facing.right.x) * bounds.extents.x +
                    Mathf.Abs(facing.right.z) * bounds.extents.z +
                    castRadius;

                float yaw =
                    Mathf.Atan2(lateral, forwardDistance) *
                    Mathf.Rad2Deg;

                float bodyAngle =
                    Mathf.Atan2(lateralRadius,
                        Mathf.Max(.1f, forwardDistance)) *
                    Mathf.Rad2Deg;

                if (yaw > halfCone + bodyAngle)
                    continue;

                float alignment =
                    forwardDistance /
                    Mathf.Sqrt(flatSqr);

                Vector3 contact =
                    collider.ClosestPoint(origin);

                Vector3 nearDelta = contact - origin;

                // Use the same physical sweep radius and optional
                // character-height sweeps as vanilla. This only
                // determines which collider to pitch toward; no
                // extra reach is added to the actual hit casts.
                float sweepReach = reach + castRadius;
                float closestSqr = nearDelta.sqrMagnitude;
                closestSqr = Mathf.Min(closestSqr,
                    (contact - origin -
                        Vector3.up * attack.m_attackHeightChar1)
                        .sqrMagnitude);
                closestSqr = Mathf.Min(closestSqr,
                    (contact - origin -
                        Vector3.up * attack.m_attackHeightChar2)
                        .sqrMagnitude);
                if (closestSqr > sweepReach * sweepReach)
                    continue;

                // Slightly inset the target's vertical bounds rather
                // than aiming at a grazing tangent or aiming for its
                // arbitrary center (bad for tall enemies).
                float inset =
                    Mathf.Min(
                        0.10f,
                        Mathf.Max(
                            0f,
                            bounds.size.y * 0.20f));

                float minY = bounds.min.y + inset;
                float maxY = bounds.max.y - inset;

                float y =
                    Mathf.Clamp(
                        origin.y,
                        minY,
                        maxY);

                // Near face of the collider in horizontal space, inset
                // slightly so a downward/upward ray enters its volume.
                Vector3 boxNear =
                    bounds.ClosestPoint(origin);

                Vector3 nearFlat =
                    boxNear - origin;
                nearFlat.y = 0f;

                float planarDistance =
                    nearFlat.magnitude;

                if (planarDistance < 0.10f)
                    planarDistance =
                        Mathf.Sqrt(flatSqr);

                planarDistance =
                    Mathf.Min(
                        Mathf.Sqrt(flatSqr),
                        planarDistance + 0.08f);

                float elevation =
                    Mathf.Atan2(
                        y - origin.y,
                        Mathf.Max(0.1f, planarDistance)) *
                    Mathf.Rad2Deg;

                if (Mathf.Abs(elevation) >
                    MaxCorrection.Value)
                {
                    continue;
                }

                float score =
                    nearDelta.sqrMagnitude *
                    (1f + (1f - alignment) * 5f);

                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestPoint =
                    origin +
                    forward * planarDistance +
                    Vector3.up * (y - origin.y);

                // The visibility test must aim at the actual enemy,
                // not our synthetic forward-only pitch vector.
                bestVisibilityPoint =
                    Vector3.Lerp(
                        contact,
                        bounds.center,
                        0.65f);
                bestUpperVisibilityPoint =
                    new Vector3(
                        bounds.center.x,
                        bounds.max.y - inset,
                        bounds.center.z);

                found = true;
            }

            if (!found)
                return;

            // On slopes a line to the feet may intersect terrain,
            // although the upper collider is plainly hittable.
            // A blocker must obstruct both the body and upper body.
            // Vanilla still performs the real collision tests.
            if (!attack.m_hitThroughWalls &&
                Physics.Linecast(
                    origin,
                    bestVisibilityPoint,
                    WorldMask,
                    QueryTriggerInteraction.Ignore) &&
                Physics.Linecast(
                    origin,
                    bestUpperVisibilityPoint,
                    WorldMask,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }

            Vector3 displacement = bestPoint - origin;
            float pitch =
                Mathf.Atan2(
                    displacement.y,
                    Mathf.Max(
                        0.1f,
                        new Vector2(
                            displacement.x,
                            displacement.z).magnitude));

            // Preserve facing yaw exactly. Only pitch changes.
            float horizontal =
                Mathf.Cos(pitch);

            direction =
                new Vector3(
                    forward.x * horizontal,
                    Mathf.Sin(pitch),
                    forward.z * horizontal);
        }
    }

    [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
    internal static class MeleeHitContextPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Attack __instance)
        {
            AutoVerticalAim.BeginHit(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            AutoVerticalAim.EndHit();
        }

        // Clear context even when another mod or vanilla throws.
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            AutoVerticalAim.EndHit();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Attack), "GetMeleeAttackDir")]
    internal static class MeleeHitDirectionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Attack __instance,
            Transform originJoint,
            ref Vector3 attackDir)
        {
            AutoVerticalAim.CorrectDirection(
                __instance,
                originJoint,
                ref attackDir);
        }
    }
}
