using System.Diagnostics;

namespace WiresBalancedCraftFromNearbyChests
{
    // Timing log for Wire's own test builds. Every call to Begin/End is
    // removed by the compiler unless the build defines WIRE_PROFILE, and so
    // is the logger below: the published DLL has none of it.
    internal static class Prof
    {
        internal const int Poll = 0, Update = 1, Rebuild = 2, ListRefresh = 3, Reindex = 4,
            CountItems = 5, CountChests = 6, HaveItem = 7, GetItem = 8, RemoveItem = 9,
            StationUse = 10, FindInChests = 11, HoverText = 12, DestroyHook = 13,
            ZdoText = 14, InventoryChanged = 15, BeamTransforms = 16, Fading = 17,
            Count = 18;

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
                "Poll (hover check)", "Update (only while busy)", "Beam rebuild", "Chest list rebuild",
                "Chest reindex", "CountItems (all)", "CountItems (chests counted)", "HaveItem", "GetItem",
                "RemoveItem", "Station use (smelter, kiln...)", "Find item in chests", "Chest hover text",
                "Destroy hook", "ZDO text set", "Inventory changed", "Beam transforms", "Fading sparks"
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
                        System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "WireBalancedCraft-Profile.log"),
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
