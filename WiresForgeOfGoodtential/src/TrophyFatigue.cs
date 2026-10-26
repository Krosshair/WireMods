using System;
using System.Globalization;

namespace WiresForgeOfGoodtential
{
    /// <summary>
    /// Per-character, per-world, per-trophy divine fatigue.
    /// Lives in Valheim's serialized player custom data, not a separate
    /// local config file, so normal character saving persists the state.
    ///
    /// The first use requires 1 Valheim day to recover; the next one
    /// before recovery adds 1.5 days (2.5 total); the third adds
    /// 2 days (4.5 total), etc. Effectiveness recovers continuously
    /// over the remaining in-game time, not wall-clock time.
    /// </summary>
    internal static class TrophyFatigue
    {
        private const string KeyPrefix =
            "wire.goodtential.fatigue.v1.";

        private sealed class Record
        {
            internal int Streak;
            internal double At;
            internal double Remaining;
            internal double Duration;
        }

        internal static float AdjustMultiplier(
            ItemDrop.ItemData trophy, float multiplier)
        {
            Player player = Player.m_localPlayer;
            if (player == null || trophy == null)
                return multiplier;

            Record record = Read(player, trophy);
            double seconds = RemainingNow(record);

            if (seconds <= 0 || record.Duration <= 0)
                return multiplier;

            // Repeated sacrifices weaken the offering more strongly.
            // Max reduction to the trophy's bonus: 90%.
            float severity =
                Math.Min(0.9f, 0.35f * record.Streak);

            float unrecovered =
                (float)Math.Min(1.0,
                    seconds / record.Duration);

            float fatigue = severity * unrecovered;

            return multiplier +
                (1f - multiplier) * fatigue;
        }

        internal static float GetRemainingDays(
            Player player, ItemDrop.ItemData trophy)
        {
            Record record = Read(player, trophy);
            double daySeconds = GetDaySeconds();

            return (float)(RemainingNow(record) /
                Math.Max(1.0, daySeconds));
        }

        internal static void RecordUse(
            Player player, ItemDrop.ItemData trophy)
        {
            if (player == null || trophy == null ||
                player.m_customData == null ||
                ZNet.instance == null)
                return;

            Record previous = Read(player, trophy);
            double remaining = RemainingNow(previous);
            int streak =
                remaining > 0
                    ? Math.Min(100, previous.Streak + 1)
                    : 1;

            // Sum of 1, 1.5, 2, ... days for consecutive uses.
            double extraDays = 1.0 +
                0.5 * (streak - 1);

            double duration =
                remaining + extraDays * GetDaySeconds();

            Record next = new Record
            {
                Streak = streak,
                At = ZNet.instance.GetTimeSeconds(),
                Remaining = duration,
                Duration = duration
            };

            player.m_customData[GetKey(trophy)] =
                next.Streak.ToString(CultureInfo.InvariantCulture) + "|" +
                next.At.ToString("R", CultureInfo.InvariantCulture) + "|" +
                next.Remaining.ToString("R", CultureInfo.InvariantCulture) + "|" +
                next.Duration.ToString("R", CultureInfo.InvariantCulture);
        }

        private static Record Read(
            Player player, ItemDrop.ItemData trophy)
        {
            Record empty = new Record();

            if (player == null || trophy == null ||
                player.m_customData == null)
                return empty;

            string stored;
            if (!player.m_customData.TryGetValue(
                GetKey(trophy), out stored))
                return empty;

            string[] parts = stored.Split('|');
            if (parts.Length != 4)
                return empty;

            int streak;
            double time, remaining, duration;
            const NumberStyles style = NumberStyles.Float;
            CultureInfo culture = CultureInfo.InvariantCulture;

            if (!int.TryParse(parts[0], out streak) ||
                !double.TryParse(parts[1], style, culture, out time) ||
                !double.TryParse(parts[2], style, culture, out remaining) ||
                !double.TryParse(parts[3], style, culture, out duration) ||
                streak < 1 || streak > 100 ||
                double.IsNaN(time) ||
                double.IsNaN(remaining) ||
                double.IsNaN(duration) ||
                double.IsInfinity(time) ||
                double.IsInfinity(remaining) ||
                double.IsInfinity(duration) ||
                remaining < 0 || duration <= 0)
                return empty;

            return new Record
            {
                Streak = streak,
                At = time,
                Remaining = remaining,
                Duration = duration
            };
        }

        private static double RemainingNow(Record record)
        {
            if (record == null ||
                record.Streak <= 0 ||
                ZNet.instance == null)
                return 0.0;

            double now = ZNet.instance.GetTimeSeconds();
            double elapsed =
                Math.Max(0.0, now - record.At);

            return Math.Max(0.0, record.Remaining - elapsed);
        }

        private static double GetDaySeconds()
        {
            if (EnvMan.instance != null)
                return Math.Max(1.0, EnvMan.instance.m_dayLengthSec);

            return 1200.0;
        }

        private static string GetKey(ItemDrop.ItemData trophy)
        {
            long world =
                ZNet.instance != null
                    ? ZNet.instance.GetWorldUID()
                    : 0L;

            string prefab =
                trophy.m_dropPrefab != null
                    ? trophy.m_dropPrefab.name
                    : trophy.m_shared != null
                        ? trophy.m_shared.m_name
                        : "unknown";

            return KeyPrefix +
                world.ToString(CultureInfo.InvariantCulture) +
                "." + prefab;
        }
    }
}
