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

        internal static bool[] ReadKeys()
        {
            ZoneSystem zone = ZoneSystem.instance;
            float now = Time.unscaledTime;
            if (zone != null && zone == cachedZone && Player.m_localPlayer == cachedPlayer &&
                now - cachedAt < 1f)
                return cached;
            // Refreshed in place: every caller reads it immediately.
            bool[] result = cached;
            cachedAt = now; cachedZone = zone; cachedPlayer = Player.m_localPlayer;
            if (zone == null) { Array.Clear(result, 0, result.Length); return result; }
            bool playerBased = IsPlayerBased();
            Player player = Player.m_localPlayer;
            for (int i = 0; i < result.Length; ++i)
                result[i] = playerBased
                    ? player != null && player.HaveUniqueKey(BossKeys[i])
                    : zone.GetGlobalKey(BossKeys[i]);
            return result;
        }

        internal static float GetBonus(int biome, float ownBonus)
        {
            if (Plugin.Enabled == null || !Plugin.Enabled.Value ||
                biome < 0 || biome >= ProgressionMath.BiomeCount)
                return 0;
            return (float)ProgressionMath.Bonus(
                biome, ReadKeys(), ownBonus,
                Plugin.LaterFactor.Value / 100f,
                Plugin.SkippedFactor.Value / 100f);
        }

        internal static bool Ready => ZoneSystem.instance != null &&
            (ZNet.instance == null || ZNet.instance.IsServer() ||
             Player.m_localPlayer != null);
    }
}
