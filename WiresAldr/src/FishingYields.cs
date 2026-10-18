using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WiresAldr
{
    // Fish caught by rod are the ONLY eligible fish: never reward
    // standing on the shore picking up escaped fish or stored fish items.
    // Mark a native catch, then grant its reward only after the game's
    // ordinary pickup actually succeeds (including delayed ownership).
    internal static class FishingYields
    {
        internal sealed class Pending
        {
            internal Player Player;
            internal float Expires;
            internal double Percent;
            internal ItemDrop.ItemData Item;
            internal int FishQuality;
            internal int FishBiomeTier;
            internal int FishXpTier;
        }
        private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        private static readonly Dictionary<int, int> hookedBiomes = new Dictionary<int, int>();
        [ThreadStatic] private static Pending pendingRpc;

        internal static double PercentForBiome(int tier, bool[] keys,
            double ownBonus, double later, double skipped)
        {
            return ProgressionMath.Bonus(tier, keys, ownBonus, later, skipped);
        }

        internal static int CatchBiome(Fish fish)
        {
            if (!fish) return -1;
            int current;
            if (!hookedBiomes.TryGetValue(fish.GetInstanceID(), out current))
                current = BiomeCatalog.FromBiome(Heightmap.FindBiome(fish.transform.position));
            if (current >= 0) return current;
            // Ocean catches have no single biome boss. Fall back to the
            // original fish-prefab progression when that mapping exists.
            string name = fish.gameObject.name.Replace("(Clone)", "").Trim();
            int mapped;
            return TierData.Enemies.TryGetValue(name, out mapped)
                ? Mathf.Clamp(mapped - 1, 0, 7) : -1;
        }

        internal static void Register(Fish fish, Character catcher)
        {
            if (!fish || !fish.IsHooked() ||
                Plugin.Enabled == null || !Plugin.Enabled.Value || !WorldProgress.Ready)
                return;
            // Extra fish (Wild fishing) and catch XP (Rewards) are separate.
            bool yields = Plugin.FishYields.Value;
            if (!yields && !(Plugin.XpEnabled.Value && Plugin.Rewards.Value)) return;
            Player player = catcher as Player;
            if (!player || player != Player.m_localPlayer) return;

            int tier = CatchBiome(fish);
            if (tier < 0) return;
            double percent = !yields || !Plugin.FishExtraCatch.Value ? 0 : PercentForBiome(tier,
                WorldProgress.ReadKeys(), Plugin.FishYieldBonus.Value,
                Plugin.LaterFactor.Value / 100.0,
                Plugin.SkippedFactor.Value / 100.0);
            percent = Math.Min(percent, Plugin.FishBonusCap.Value);
            // All successful rod catches can earn completion XP, even before
            // biome progression grants an extra fish yield.

            ItemDrop worldFish = fish.GetComponent<ItemDrop>();
            ItemDrop.ItemData item = null;
            if (worldFish != null) item = worldFish.m_itemData.Clone();
            else if (fish.m_pickupItem != null)
            {
                ItemDrop prefab = fish.m_pickupItem.GetComponent<ItemDrop>();
                if (prefab != null) item = prefab.m_itemData.Clone();
            }
            if (item == null) return;
            item.m_stack = 1;
            if (AldrDiagnostics.Active) AldrDiagnostics.Sample("FISH_ELIGIBLE",
                "fish=" + fish.gameObject.name + " biome_tier=" + (tier + 1) +
                " expected_extra_chance_percent=" + percent.ToString("0.##") +
                " (pending successful pickup)");
            pending[fish.GetInstanceID()] = new Pending
            {
                Player = player,
                Expires = Time.time + 30f,
                Percent = percent,
                Item = item,
                FishQuality = Mathf.Max(1, item.m_quality),
                FishBiomeTier = tier,
                // 1-based: the later of where it was hooked and where it is native.
                FishXpTier = Mathf.Max(tier + 1, DynamicExperience.GetFishTier(fish))
            };
            // Avoid retaining destroyed Unity objects indefinitely.
            if (pending.Count > 128)
            {
                var expired = new List<int>();
                foreach (var kv in pending)
                    if (kv.Value.Expires < Time.time) expired.Add(kv.Key);
                foreach (int id in expired) pending.Remove(id);
            }
        }

        internal static Pending Find(Fish fish)
        {
            if (!fish) return null;
            Pending reward;
            if (!pending.TryGetValue(fish.GetInstanceID(), out reward)) return null;
            if (Time.time > reward.Expires || !reward.Player)
            {
                pending.Remove(fish.GetInstanceID());
                return null;
            }
            return reward;
        }

        internal static void Complete(Pending reward, Player player)
        {
            if (reward == null || player != reward.Player) return;
            // Exactly once: take entry before adding items.
            int key = 0; bool found = false;
            foreach (var kv in pending)
                if (ReferenceEquals(kv.Value, reward)) { key = kv.Key; found = true; break; }
            if (!found) return;
            pending.Remove(key);
            // Reward a real fish only when vanilla pickup succeeded.
            float quality = Mathf.Clamp(reward.FishQuality, 1, 5);
            // Catch bonus: 3 XP, +1.5 per quality level above 1.
            DynamicExperience.WithFishCompletion(player, reward.FishXpTier, () =>
                SkillReward.Grant(player, Skills.SkillType.Fishing,
                    Plugin.FishXp.Value + Plugin.FishXpPerQuality.Value * (quality - 1f), "fish_caught_quality_" + reward.FishQuality));

            int extra = ProgressionMath.ExtraCount(1, reward.Percent, UnityEngine.Random.value);
            if (AldrDiagnostics.Active) AldrDiagnostics.Sample("FISH_CAUGHT", "rod pickup confirmed, extra_fish=" +
                extra + " expected_chance_percent=" + reward.Percent.ToString("0.##"));
            if (extra <= 0) return;
            var inventory = player.GetInventory();
            if (inventory == null) return;
            for (int i = 0; i < extra; i++)
            {
                ItemDrop.ItemData copy = reward.Item.Clone();
                copy.m_stack = 1;
                try
                {
                    if (!inventory.AddItem(copy))
                        ItemDrop.DropItem(copy, 1, player.transform.position + player.transform.forward, Quaternion.identity);
                }
                catch (Exception e)
                {
                    // An optional extra must never break vanilla's successful pickup.
                    Debug.LogWarning("[Aldr] Could not award extra fish: " + e.Message);
                }
            }
        }

        [HarmonyPatch(typeof(Fish), nameof(Fish.OnHooked))]
        private static class HookLocation
        {
            [HarmonyPrefix]
            private static void Prefix(Fish __instance, FishingFloat ff)
            {
                if (!__instance) return;
                int id = __instance.GetInstanceID();
                if (ff == null) hookedBiomes.Remove(id);
                else hookedBiomes[id] = BiomeCatalog.FromBiome(Heightmap.FindBiome(__instance.transform.position));
                if (hookedBiomes.Count > 128) hookedBiomes.Clear();
            }
        }

        [HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.Catch))]
        private static class RodCatch
        {
            [HarmonyPrefix]
            private static void Prefix(Fish fish, Character owner)
            {
                Register(fish, owner);
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup),
            new[] { typeof(GameObject), typeof(bool), typeof(bool) })]
        private static class WorldFishPickup
        {
            [HarmonyPrefix]
            private static void Prefix(GameObject go, out Pending __state)
            {
                Fish f = go ? go.GetComponent<Fish>() : null;
                __state = Find(f);
            }

            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance, bool __result, Pending __state)
            {
                if (__result && __instance is Player p)
                    Complete(__state, p);
            }
        }

        // Fish without an ItemDrop follow their own asynchronous pickup RPC.
        [HarmonyPatch(typeof(Fish), "RPC_Pickup")]
        private static class FishRpcPickup
        {
            [HarmonyPrefix]
            private static void Prefix(Fish __instance)
            {
                pendingRpc = Find(__instance);
            }
            [HarmonyPostfix]
            private static void Postfix() { pendingRpc = null; }
            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception) { pendingRpc = null; return __exception; }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.PickupPrefab),
            new[] { typeof(GameObject), typeof(int), typeof(bool) })]
        private static class FishPrefabPickup
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance, ItemDrop.ItemData __result)
            {
                if (__result != null && pendingRpc != null && __instance is Player p)
                    Complete(pendingRpc, p);
            }
        }
    }
}
