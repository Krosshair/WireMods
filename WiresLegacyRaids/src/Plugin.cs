using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WiresLegacyRaids
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wire.valheim.legacyraids";
        public const string Name = "Wire's Legacy Raids";
        public const string Version = "0.1.1";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> BonusChance;
        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enable bonus rolls for retired progression raids.");

            BonusChance = Config.Bind(
                "General",
                "Bonus Legacy Raid Chance",
                10f,
                new ConfigDescription(
                    "Chance, in percent, for a retired progression raid after a normal raid roll does not start anything.",
                    new AcceptableValueRange<float>(0f, 100f)));

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            Logger.LogInfo(Name + " " + Version + " loaded.");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchSelf();

            Log = null;
        }
    }

    internal static class LegacyRaidController
    {
        private sealed class Candidate
        {
            internal RandomEvent Event;
            internal readonly List<Vector3> Points = new List<Vector3>();
        }

        private static readonly System.Random Rng = new System.Random();

        private static readonly AccessTools.FieldRef<RandEventSystem, float>
            EventTimer =
                AccessTools.FieldRefAccess<RandEventSystem, float>(
                    "m_eventTimer");

        private static readonly MethodInfo RefreshPlayerEventData =
            AccessTools.Method(
                typeof(RandEventSystem),
                "RefreshPlayerEventData");

        private static readonly FieldInfo PlayerEventDatas =
            AccessTools.Field(
                typeof(RandEventSystem),
                "s_playerEventDatas");

        internal static bool WillRollThisTick(
            RandEventSystem system,
            float dt)
        {
            if (system == null ||
                ZNet.instance == null ||
                !ZNet.instance.IsServer() ||
                Game.m_eventRate <= 0f)
            {
                return false;
            }

            float threshold =
                system.m_eventIntervalMin *
                60f *
                Game.m_eventRate;

            return EventTimer(system) + dt > threshold;
        }

        internal static void TryStartLegacyRaid(
            RandEventSystem system)
        {
            if (system == null ||
                Plugin.Enabled == null ||
                !Plugin.Enabled.Value ||
                Plugin.BonusChance == null ||
                Plugin.BonusChance.Value <= 0f ||
                ZNet.instance == null ||
                !ZNet.instance.IsServer() ||
                system.GetCurrentRandomEvent() != null)
            {
                return;
            }

            if (Rng.NextDouble() * 100.0 >
                Plugin.BonusChance.Value)
            {
                return;
            }

            List<Candidate> candidates =
                BuildCandidates(system);

            if (candidates.Count == 0)
                return;

            Candidate chosen =
                candidates[Rng.Next(candidates.Count)];

            Vector3 point =
                chosen.Points[Rng.Next(chosen.Points.Count)];

            system.SetRandomEventByName(
                chosen.Event.m_name,
                point);

            Plugin.Log.LogInfo(
                "Legacy raid started: " +
                chosen.Event.m_name +
                " (" + candidates.Count +
                " eligible retired raids)");
        }

        private static List<Candidate> BuildCandidates(
            RandEventSystem system)
        {
            List<Candidate> result =
                new List<Candidate>();

            if (ZoneSystem.instance == null ||
                WorldGenerator.instance == null ||
                system.m_events == null)
            {
                return result;
            }

            RefreshPlayerEventData?.Invoke(null, null);

            List<RandEventSystem.PlayerEventData> players =
                PlayerEventDatas?.GetValue(null)
                as List<RandEventSystem.PlayerEventData>;

            if (players == null ||
                players.Count == 0)
            {
                return result;
            }

            bool playerBasedEvents =
                ZoneSystem.instance.GetGlobalKey(
                    GlobalKeys.PlayerEvents);

            // Player-based raids: vanilla decides eligibility on each
            // client and only tells the server the final list, which never
            // contains retired raids. Clients running this mod publish
            // their retired-by-boss raids separately (see below).
            if (playerBasedEvents)
                return BuildPlayerBasedCandidates(system);

            foreach (RandomEvent ev in system.m_events)
            {
                if (!IsRetiredProgressionRaid(ev))
                    continue;

                Candidate candidate =
                    new Candidate { Event = ev };

                foreach (
                    RandEventSystem.PlayerEventData player
                    in players)
                {
                    if (!IsValidPoint(
                            ev,
                            player,
                            playerBasedEvents))
                    {
                        continue;
                    }

                    candidate.Points.Add(
                        player.position);
                }

                if (candidate.Points.Count > 0)
                    result.Add(candidate);
            }

            return result;
        }

        internal const string LegacyKey = "wire.legacyRaids";

        private struct LegacyPlayer
        {
            internal Vector3 Position;
            internal int BaseValue;
            internal HashSet<string> Legacy;
        }

        private static bool ReadPlayer(Dictionary<string, string> data, Vector3 position, out LegacyPlayer player)
        {
            player = default(LegacyPlayer);
            string legacy, baseValue;
            if (data == null || !data.TryGetValue(LegacyKey, out legacy) || string.IsNullOrEmpty(legacy) ||
                !data.TryGetValue("baseValue", out baseValue) || !int.TryParse(baseValue, out player.BaseValue))
                return false;
            player.Position = position;
            player.Legacy = new HashSet<string>(legacy.Split(','));
            return true;
        }

        private static List<Candidate> BuildPlayerBasedCandidates(RandEventSystem system)
        {
            var result = new List<Candidate>();
            var players = new List<LegacyPlayer>();
            LegacyPlayer p;
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null &&
                ReadPlayer(ZNet.instance.m_serverSyncedPlayerData, ZNet.instance.GetReferencePosition(), out p))
                players.Add(p);
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                if (peer.IsReady() && ReadPlayer(peer.m_serverSyncedPlayerData, peer.m_refPos, out p))
                    players.Add(p);
            if (players.Count == 0) return result;

            foreach (RandomEvent ev in system.m_events)
            {
                if (ev == null || !ev.m_enabled || ev.m_devDisabled || !ev.m_random ||
                    ev.m_standaloneInterval > 0f || string.IsNullOrEmpty(ev.m_name))
                    continue;
                var candidate = new Candidate { Event = ev };
                foreach (LegacyPlayer player in players)
                {
                    if (!player.Legacy.Contains(ev.m_name) || player.Position.y > 3000f) continue;
                    if (ev.m_nearBaseOnly && player.BaseValue < 3) continue;
                    if (ev.m_biome != Heightmap.Biome.None &&
                        (WorldGenerator.instance.GetBiome(player.Position) & ev.m_biome) == Heightmap.Biome.None)
                        continue;
                    candidate.Points.Add(player.Position);
                }
                if (candidate.Points.Count > 0) result.Add(candidate);
            }
            return result;
        }

        // Client side: the same per-player check vanilla uses, except that
        // a boss-defeat key that retired the raid does not block it.
        internal static bool IsPlayerLegacyRaid(Player player, RandomEvent ev)
        {
            if (player == null || ev == null || !ev.m_enabled || ev.m_devDisabled ||
                !ev.m_random || ev.m_standaloneInterval > 0f || string.IsNullOrEmpty(ev.m_name))
                return false;
            bool retired = false;
            foreach (string key in ev.m_altNotRequiredPlayerKeys)
            {
                if (!player.HaveUniqueKey(key)) continue;
                if (!key.StartsWith("defeated_", StringComparison.OrdinalIgnoreCase)) return false;
                retired = true;
            }
            if (!retired) return false;
            foreach (ItemDrop item in ev.m_altRequiredNotKnownItems)
                if (player.IsMaterialKnown(item.m_itemData.m_shared.m_name)) return false;
            foreach (ItemDrop item in ev.m_altRequiredKnownItems)
                if (player.IsMaterialKnown(item.m_itemData.m_shared.m_name)) return true;
            foreach (string key in ev.m_altRequiredPlayerKeysAny)
                if (player.HaveUniqueKey(key)) return true;
            foreach (string key in ev.m_altRequiredPlayerKeysAll)
                if (!player.HaveUniqueKey(key)) return false;
            return ev.m_altRequiredKnownItems.Count <= 0 && ev.m_altRequiredPlayerKeysAny.Count <= 0;
        }

        private static bool IsRetiredProgressionRaid(
            RandomEvent ev)
        {
            if (ev == null ||
                !ev.m_enabled ||
                ev.m_devDisabled ||
                !ev.m_random ||
                ev.m_standaloneInterval > 0f ||
                string.IsNullOrEmpty(ev.m_name))
            {
                return false;
            }

            if (ev.m_requiredGlobalKeys != null)
            {
                foreach (
                    string required
                    in ev.m_requiredGlobalKeys)
                {
                    if (!ZoneSystem.instance
                            .GetGlobalKey(required))
                    {
                        return false;
                    }
                }
            }

            if (ev.m_notRequiredGlobalKeys == null ||
                ev.m_notRequiredGlobalKeys.Count == 0)
            {
                return false;
            }

            bool retiredByBossProgression = false;

            foreach (
                string forbidden
                in ev.m_notRequiredGlobalKeys)
            {
                if (string.IsNullOrEmpty(forbidden) ||
                    !ZoneSystem.instance
                        .GetGlobalKey(forbidden))
                {
                    continue;
                }

                // Only bypass the boss-defeat key that retired the
                // raid. Other vanilla exclusion keys remain binding.
                if (!forbidden.StartsWith(
                        "defeated_",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                retiredByBossProgression = true;
            }

            if (!retiredByBossProgression)
                return false;

            // Under PlayerEvents, vanilla uses player-specific
            // eligibility instead of global keys for events with
            // alternate criteria. Those events are not retired by
            // global boss defeat in the first place.
            if (ZoneSystem.instance.GetGlobalKey(
                    GlobalKeys.PlayerEvents) &&
                HasAlternatePlayerRequirements(ev))
            {
                return false;
            }

            return true;
        }

        private static bool HasAlternatePlayerRequirements(
            RandomEvent ev)
        {
            return ev.m_altRequiredKnownItems.Count > 0 ||
                   ev.m_altRequiredNotKnownItems.Count > 0 ||
                   ev.m_altNotRequiredPlayerKeys.Count > 0 ||
                   ev.m_altRequiredPlayerKeysAny.Count > 0 ||
                   ev.m_altRequiredPlayerKeysAll.Count > 0;
        }

        private static bool IsValidPoint(
            RandomEvent ev,
            RandEventSystem.PlayerEventData player,
            bool playerBasedEvents)
        {
            if (player.position.y > 3000f)
                return false;

            if (ev.m_nearBaseOnly &&
                player.baseValue < 3)
            {
                return false;
            }

            // Match the original PlayerEvents eligibility check:
            // without this player's event unlock, do not raid them.
            if (playerBasedEvents &&
                (player.possibleEvents == null ||
                 !player.possibleEvents.Contains(
                     ev.m_name)))
            {
                return false;
            }

            // Always preserve vanilla's biome limits. Only the
            // *retirement* progression key can be bypassed.
            if (ev.m_biome != Heightmap.Biome.None &&
                (WorldGenerator.instance
                    .GetBiome(player.position) &
                    ev.m_biome) ==
                    Heightmap.Biome.None)
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RandEventSystem), "UpdateRandomEvent")]
    internal static class RandomEventRollPatch
    {
        private struct RollState
        {
            internal bool Due;
            internal bool HadEvent;
        }

        [HarmonyPrefix]
        private static void Prefix(
            RandEventSystem __instance,
            float dt,
            ref RollState __state)
        {
            __state = new RollState
            {
                Due =
                    LegacyRaidController
                        .WillRollThisTick(
                            __instance,
                            dt),
                HadEvent =
                    __instance
                        .GetCurrentRandomEvent()
                    != null
            };
        }

        [HarmonyPostfix]
        private static void Postfix(
            RandEventSystem __instance,
            RollState __state)
        {
            if (!__state.Due ||
                __state.HadEvent ||
                __instance.GetCurrentRandomEvent()
                    != null)
            {
                return;
            }

            LegacyRaidController
                .TryStartLegacyRaid(__instance);
        }
    }

    // Publishes this player's retired-by-boss raids next to vanilla's own
    // "possibleEvents", whenever vanilla refreshes that list.
    [HarmonyPatch(typeof(Player), nameof(Player.UpdateEvents))]
    internal static class PublishLegacyRaids
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer || ZNet.instance == null ||
                RandEventSystem.instance == null || Plugin.Enabled == null || !Plugin.Enabled.Value)
                return;
            var names = new List<string>();
            foreach (RandomEvent ev in RandEventSystem.instance.m_events)
                if (LegacyRaidController.IsPlayerLegacyRaid(__instance, ev))
                    names.Add(ev.m_name);
            ZNet.instance.m_serverSyncedPlayerData[LegacyRaidController.LegacyKey] = string.Join(",", names.ToArray());
        }
    }
}
