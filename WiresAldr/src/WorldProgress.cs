using System;
using UnityEngine;

namespace WiresAldr
{
    internal static class WorldProgress
    {
        // Same keys that Valheim uses for Forsaken progression.
        private static readonly string[] BossKeys = {
            "defeated_eikthyr", "defeated_gdking", "defeated_bonemass",
            "defeated_dragon", "defeated_goblinking", "defeated_queen",
            "defeated_fader", "defeated_frozenking_p3"
        };

        internal static bool IsPlayerBased()
        {
            return ZoneSystem.instance != null &&
                ZoneSystem.instance.GetGlobalKey(GlobalKeys.PlayerEvents);
        }

        // Read-only snapshot, refreshed at most once per second: callers
        // (drops, picks, fish spawns, every XP gain) must not modify or keep it.
        private static bool[] cached = new bool[ProgressionMath.BiomeCount];
        private static float cachedAt = -999f;
        private static ZoneSystem cachedZone;
        private static Player cachedPlayer;

        // PlayerEvents tracks each player's keys separately. Spawn callbacks
        // pass their location so the nearest present player supplies the keys
        // (not necessarily the local owner in multiplayer).
        // Never substitute server-wide keys for player-specific progression.
        // Calls without a location retain the local player's perspective.
        internal static bool[] ReadKeys(Vector3? location = null)
        {
            ZoneSystem zone = ZoneSystem.instance;
            bool playerBased = zone != null && IsPlayerBased();
            Player player = playerBased ? Player.m_localPlayer : null;
            if (playerBased && location.HasValue)
            {
                Player closest = null;
                float nearest = float.PositiveInfinity;
                foreach (Player candidate in Player.GetAllPlayers())
                {
                    if (candidate == null) continue;
                    float d = (candidate.transform.position - location.Value).sqrMagnitude;
                    if (d < nearest) { nearest = d; closest = candidate; }
                }
                if (closest != null) player = closest;
            }
            float now = Time.unscaledTime;
            if (zone != null && zone == cachedZone && player == cachedPlayer &&
                now - cachedAt < 1f)
                return cached;
            // Refreshed in place: callers must not retain the result.
            bool[] result = cached;
            cachedAt = now; cachedZone = zone; cachedPlayer = player;
            if (zone == null) { Array.Clear(result, 0, result.Length); return result; }
            for (int i = 0; i < result.Length; ++i)
                result[i] = playerBased
                    ? player != null && player.HaveUniqueKey(BossKeys[i])
                    : zone.GetGlobalKey(BossKeys[i]);
            return result;
        }

        internal static float GetBonus(int biome, float ownBonus, Vector3? location = null)
        {
            if (Plugin.Enabled == null || !Plugin.Enabled.Value ||
                biome < 0 || biome >= ProgressionMath.BiomeCount)
                return 0;
            return (float)ProgressionMath.Bonus(
                biome, ReadKeys(location), ownBonus,
                Plugin.LaterFactor.Value / 100f,
                Plugin.SkippedFactor.Value / 100f);
        }

        internal static bool Ready => ZoneSystem.instance != null &&
            (ZNet.instance == null || ZNet.instance.IsServer() ||
             Player.m_localPlayer != null);
    }
}
