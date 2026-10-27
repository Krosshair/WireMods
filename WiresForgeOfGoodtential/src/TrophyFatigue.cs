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

            string key = GetKey(trophy);
            Cache.Remove(key);
            player.m_customData[key] =
                next.Streak.ToString(CultureInfo.InvariantCulture) + "|" +
                next.At.ToString("R", CultureInfo.InvariantCulture) + "|" +
                next.Remaining.ToString("R", CultureInfo.InvariantCulture) + "|" +
                next.Duration.ToString("R", CultureInfo.InvariantCulture);
        }

        // Parsed records, so the odds display doesn't re-read and
        // re-parse the save data every time it asks. Cleared when a
        // trophy is used or the player/world changes.
        private static readonly System.Collections.Generic.Dictionary<string, Record> Cache =
            new System.Collections.Generic.Dictionary<string, Record>();
        private static Player _cachePlayer;
        private static readonly Record Empty = new Record();

        private static Record Read(
            Player player, ItemDrop.ItemData trophy)
        {
            if (player == null || trophy == null ||
                player.m_customData == null)
                return Empty;

            if (!ReferenceEquals(player, _cachePlayer))
            {
                Cache.Clear();
                _cachePlayer = player;
            }

            string key = GetKey(trophy);
            Record cached;
            if (Cache.TryGetValue(key, out cached))
                return cached;

            cached = Parse(player, key);
            Cache[key] = cached;
            return cached;
        }

        private static Record Parse(Player player, string key)
        {
            Record empty = Empty;

            string stored;
            if (!player.m_customData.TryGetValue(
                key, out stored))
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

        // In-game days until this trophy type is fully rested; 0 if it is.
        internal static float RestingDays(ItemDrop.ItemData trophy)
        {
            Player player = Player.m_localPlayer;
            if (player == null || trophy == null)
                return 0f;
            return (float)(RemainingNow(Read(player, trophy)) / GetDaySeconds());
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

        private static readonly System.Collections.Generic.Dictionary<string, string> Keys =
            new System.Collections.Generic.Dictionary<string, string>();
        private static long _keysWorld;

        private static string GetKey(ItemDrop.ItemData trophy)
        {
            long worldNow = ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0L;
            if (worldNow != _keysWorld)
            {
                Keys.Clear();
                Cache.Clear();
                _keysWorld = worldNow;
            }
            string name = trophy.m_dropPrefab != null ? trophy.m_dropPrefab.name
                : trophy.m_shared != null ? trophy.m_shared.m_name : "unknown";
            string built;
            if (Keys.TryGetValue(name, out built))
                return built;
            built = BuildKey(trophy);
            Keys[name] = built;
            return built;
        }

        private static string BuildKey(ItemDrop.ItemData trophy)
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
