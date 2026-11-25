using System;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // One rule for durability, applied only when vanilla actually uses the item.
    // No target categories, item tags, extra updates, or independent wear timers.
    internal static class ImpactDurability
    {
        internal static bool On => Plugin.Enabled != null && Plugin.Enabled.Value;
        internal static float Jitter() => UnityEngine.Random.Range(0.88f, 1.12f);
        // Offensive equipment should vary very little around vanilla wear.
        private static float UseJitter() => UnityEngine.Random.Range(0.98f, 1.02f);
        internal static bool Wears(ItemDrop.ItemData item) =>
            item != null && item.m_shared != null && item.m_shared.m_useDurability &&
            item.m_durability > 0f;

        // 50% of maximum health lost in a hit ~50 total durability across armor,
        // before the small random variation. Caps prevent extreme one-hit wear.
        internal static float WearFromHit(float damage, float maxHealth, float cap) =>
            Mathf.Min(cap, 100f * Mathf.Max(0f, damage) / Mathf.Max(1f, maxHealth)) *
            Jitter() * Game.m_durabilityRate;

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Chest =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_chestItem");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Legs =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_legItem");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Helmet =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_helmetItem");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Cape =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_shoulderItem");

        // Replace vanilla's one-random-piece, full-damage deduction.
        // All armor shares one roll; the cape takes only 2%, not a full share.
        [HarmonyPatch(typeof(Player), "DamageArmorDurability")]
        private static class Armor
        {
            private static bool Prefix(Player __instance, HitData hit)
            {
                if (!On) return true;
                if (__instance == null || hit == null) return false;
                float damage = hit.GetTotalPhysicalDamage() + hit.GetTotalElementalDamage();
                if (damage <= 0f) return false;
                float total = WearFromHit(damage, __instance.GetMaxHealth(), 65f);
                ItemDrop.ItemData chest = Chest(__instance), legs = Legs(__instance),
                    helmet = Helmet(__instance), cape = Cape(__instance);
                float a = Wears(chest) ? UnityEngine.Random.Range(0.75f, 1.25f) : 0f;
                float b = Wears(legs) ? UnityEngine.Random.Range(0.75f, 1.25f) : 0f;
                float c = Wears(helmet) ? UnityEngine.Random.Range(0.75f, 1.25f) : 0f;
                float sum = a + b + c;
                bool hasMainArmor = sum > 0f;
                // With only a cape equipped, still give it modest wear instead
                // of silently losing 98% of the budget; never treat it as a
                // full chest/helmet substitute.
                float capeWear = Wears(cape) ? (hasMainArmor ? total * 0.02f
                    : Mathf.Min(3f * Game.m_durabilityRate, total * 0.10f)) : 0f;
                float main = total - capeWear;
                if (sum > 0f)
                {
                    if (a > 0f) chest.m_durability = Mathf.Max(0f, chest.m_durability - main * a / sum);
                    if (b > 0f) legs.m_durability = Mathf.Max(0f, legs.m_durability - main * b / sum);
                    if (c > 0f) helmet.m_durability = Mathf.Max(0f, helmet.m_durability - main * c / sum);
                }
                if (capeWear > 0f)
                    cape.m_durability = Mathf.Max(0f, cape.m_durability - capeWear);
                return false;
            }
        }

        private static readonly Func<Humanoid, ItemDrop.ItemData> CurrentBlocker =
            AccessTools.MethodDelegate<Func<Humanoid, ItemDrop.ItemData>>(
                AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker"));
        private struct BlockState
        {
            internal ItemDrop.ItemData Item;
            internal float Before, Incoming, MaxHealth;
        }

        // Vanilla uses the equipped left-hand item or, if absent, the weapon.
        // Use the same item vanilla selected, then replace its vanilla wear.
        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        private static class Block
        {
            private static void Prefix(Humanoid __instance, HitData hit, out BlockState __state)
            {
                __state = default;
                if (!On || !(__instance is Player p) || hit == null || CurrentBlocker == null) return;
                var item = CurrentBlocker(__instance);
                if (!Wears(item)) return;
                __state.Item = item;
                __state.Before = item.m_durability;
                __state.Incoming = hit.GetTotalBlockableDamage();
                __state.MaxHealth = p.GetMaxHealth();
            }

            private static void Postfix(BlockState __state)
            {
                ItemDrop.ItemData item = __state.Item;
                if (item == null || item.m_durability >= __state.Before) return;
                // Blocking a 50%-health-strength hit costs about 22.5 points,
                // regardless of whether a shield or weapon did the blocking.
                float cost = Mathf.Min(35f * Game.m_durabilityRate,
                    WearFromHit(__state.Incoming, __state.MaxHealth, 100f) * 0.45f);
                item.m_durability = Mathf.Max(0f, __state.Before - cost);
            }
        }

        private static readonly AccessTools.FieldRef<Attack, Humanoid> Attacker =
            AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
        [ThreadStatic] private static bool s_inAction;
        [ThreadStatic] private static float s_largestHit;

        private struct AttackState
        {
            internal ItemDrop.ItemData Weapon;
            internal float Before;
        }
        private static void Begin(Attack attack, out AttackState state)
        {
            state = default;
            s_inAction = false;
            s_largestHit = 0f;
            if (!On || attack == null || !(Attacker(attack) is Player)) return;
            var weapon = attack.GetWeapon();
            if (!Wears(weapon)) return;
            state.Weapon = weapon;
            state.Before = weapon.m_durability;
            // Projectiles do their actual damage calculation in a later
            // method, after the firing durability drain has occurred.
            // The weapon's base damage is a safe output-strength estimate.
            s_largestHit = weapon.GetDamage().GetTotalDamage();
            s_inAction = true;
        }
        private static void Finish(AttackState state)
        {
            s_inAction = false;
            ItemDrop.ItemData item = state.Weapon;
            if (item == null || item.m_durability >= state.Before) return;
            float vanilla = state.Before - item.m_durability;
            // Keep the item's original drain as baseline. The output-damage
            // dependence is deliberately slight, with about +/- 5% total.
            float impact = Mathf.Clamp01(s_largestHit / 150f);
            float factor = (0.97f + 0.06f * impact) * UseJitter();
            item.m_durability = Mathf.Max(0f, state.Before - vanilla * factor);
        }

        // After vanilla computes the randomized outgoing hit damage.
        [HarmonyPatch(typeof(Attack), "ModifyDamage")]
        private static class Output
        {
            private static void Postfix(HitData hitData)
            {
                if (s_inAction && hitData != null)
                    s_largestHit = Mathf.Max(s_largestHit, hitData.GetTotalDamage());
            }
        }

        [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
        private static class Melee
        {
            private static void Prefix(Attack __instance, out AttackState __state) => Begin(__instance, out __state);
            private static void Postfix(AttackState __state) => Finish(__state);
            private static Exception Finalizer(Exception __exception) { s_inAction = false; return __exception; }
        }
        [HarmonyPatch(typeof(Attack), "DoAreaAttack")]
        private static class Area
        {
            private static void Prefix(Attack __instance, out AttackState __state) => Begin(__instance, out __state);
            private static void Postfix(AttackState __state) => Finish(__state);
            private static Exception Finalizer(Exception __exception) { s_inAction = false; return __exception; }
        }
        [HarmonyPatch(typeof(Attack), "ProjectileAttackTriggered")]
        private static class Projectile
        {
            private static void Prefix(Attack __instance, out AttackState __state) => Begin(__instance, out __state);
            private static void Postfix(AttackState __state) => Finish(__state);
            private static Exception Finalizer(Exception __exception) { s_inAction = false; return __exception; }
        }
        [HarmonyPatch(typeof(Attack), "DoNonAttack")]
        private static class NonAttack
        {
            private static void Prefix(Attack __instance, out AttackState __state) => Begin(__instance, out __state);
            private static void Postfix(AttackState __state) => Finish(__state);
            private static Exception Finalizer(Exception __exception) { s_inAction = false; return __exception; }
        }
    }
}
