using System.Diagnostics;

namespace EnemyHudRework
{
    // Timing log for Wire's own test builds. Every call to Begin/End is
    // removed by the compiler unless the build defines WIRE_PROFILE, and so
    // is the logger below: the published DLL has none of it.
    internal static class Prof
    {
        internal const int Frame = 0,
            Look = 1,
            Ranks = 2,
            Scale = 3,
            Name = 4,
            Stagger = 5,
            Bubble = 6,
            Health = 7,
            Prediction = 8,
            Outline = 9,
            SharedRanks = 10,
            Weakness = 11,
            BossDps = 12,
            Enhanced = 13,
            Status = 14,
            Flash = 15,
            Viewport = 16,
            PlayerOpacity = 17,
            IconFlashes = 18,
            StatusChanged = 19,
            DamageRpc = 20,
            DamagePrefix = 21,
            ShowHud = 22,
            Count = 23;

        [Conditional("WIRE_PROFILE")]
        internal static void Begin(int section)
        {
#if WIRE_PROFILE
            Timing.Begin(section);
#endif
        }

        [Conditional("WIRE_PROFILE")]
        internal static void End(int section)
        {
#if WIRE_PROFILE
            Timing.End(section);
#endif
        }

#if WIRE_PROFILE
        private static class Timing
        {
            private static readonly string[] Names =
            {
                "All nameplates (per frame)", "Look target", "Pending resist ranks", "Bar scale", "Name / stars", "Stagger bar", "World bubble", "Health value", "Hit prediction", "Critical outline", "Shared ranks", "Weakness icons", "Boss DPS", "Enhanced effects", "Status icons", "Frame flash", "Viewport visibility", "Player opacity", "Icon flashes", "Status effect start/stop", "Damage RPC (hit response)", "Damage prefix", "Nameplate created"
            };

            private static readonly long[] Started = new long[Count];
            private static readonly int[] Depth = new int[Count];
            private static readonly long[] Total = new long[Count];
            private static readonly long[] Max = new long[Count];
            private static readonly int[] Calls = new int[Count];
            private static readonly int[] Slow = new int[Count];
            private static long _intervalStart = Stopwatch.GetTimestamp();
            private static int _frameStart = -1;
            private static readonly long SlowTicks = Stopwatch.Frequency / 1000; // 1 ms
            private static readonly long IntervalTicks = Stopwatch.Frequency * 30;

            static Timing()
            {
                // The last stretch is written when the game closes too.
                UnityEngine.Application.quitting += () => Write(Stopwatch.GetTimestamp());
            }

            internal static void Begin(int s)
            {
                if (Depth[s]++ == 0)
                    Started[s] = Stopwatch.GetTimestamp();
            }

            internal static void End(int s)
            {
                if (Depth[s] == 0 || --Depth[s] > 0)
                    return;
                long now = Stopwatch.GetTimestamp();
                long took = now - Started[s];
                Total[s] += took;
                Calls[s]++;
                if (took > Max[s]) Max[s] = took;
                if (took > SlowTicks) Slow[s]++;
                if (_frameStart < 0) _frameStart = UnityEngine.Time.frameCount;
                if (now - _intervalStart >= IntervalTicks)
                    Write(now);
            }

            private static void Write(long now)
            {
                try
                {
                    double seconds = (now - _intervalStart) / (double)Stopwatch.Frequency;
                    int frames = UnityEngine.Time.frameCount - _frameStart;
                    var sb = new System.Text.StringBuilder();
                    sb.Append(System.DateTime.Now.ToString("HH:mm:ss"))
                      .Append("  last ").Append(seconds.ToString("0")).Append(" s, ")
                      .Append(frames).Append(" frames\n");
                    double totalMs = 0;
                    for (int i = 0; i < Count; i++)
                    {
                        if (Calls[i] == 0) continue;
                        double ms = Total[i] * 1000.0 / Stopwatch.Frequency;
                        totalMs += ms;
                        sb.Append("  ").Append(Names[i].PadRight(32))
                          .Append(Calls[i].ToString().PadLeft(8)).Append(" calls ")
                          .Append(ms.ToString("0.00").PadLeft(9)).Append(" ms total ")
                          .Append((ms * 1000.0 / Calls[i]).ToString("0.0").PadLeft(8)).Append(" us avg ")
                          .Append((Max[i] * 1e6 / Stopwatch.Frequency).ToString("0").PadLeft(7)).Append(" us max ")
                          .Append(Slow[i].ToString().PadLeft(5)).Append(" over 1 ms\n");
                        Calls[i] = 0; Total[i] = 0; Max[i] = 0; Slow[i] = 0;
                    }
                    sb.Append("  Sum ").Append(totalMs.ToString("0.00")).Append(" ms = ")
                      .Append(frames > 0 ? (totalMs * 1000.0 / frames).ToString("0.0") : "0")
                      .Append(" us per frame (sections can include each other)\n\n");
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "WireEnemyHud-Profile.log"),
                        sb.ToString());
                }
                catch
                {
                }
                _intervalStart = now;
                _frameStart = UnityEngine.Time.frameCount;
            }
        }
#endif
    }
}
