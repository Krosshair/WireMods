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

        internal static bool[] ReadKeys()
        {
            var result = new bool[ProgressionMath.BiomeCount];
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return result;
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
