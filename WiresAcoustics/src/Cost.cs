namespace WiresAcoustics
{
    // Main-thread time this mod takes (Debug log only): total and worst single call per report.
    internal static class Cost
    {
        internal struct Stat { internal long Total, Worst; internal int Calls; internal bool WorstGc; }
        internal static Stat Tick, Scan, Sound, Measure, Batch, Recheck, Follow, Dirs, Rays, Rest, Budget, Travel;

        internal static long Start() => Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

        internal static void Add(ref Stat s, long start)
        {
            if (start == 0L) return;
            long t = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            s.Total += t; s.Calls++;
            if (t > s.Worst) s.Worst = t;
        }

        internal static void AddTicks(ref Stat s, long t)
        {
            s.Total += t; s.Calls++;
            if (t > s.Worst) s.Worst = t;
        }

        // The whole tick, and whether the garbage collector ran during the slowest one (its pause is
        // counted as ours, though anything in the game can be what filled the heap).
        internal static void AddTick(long start, int gcBefore)
        {
            if (start == 0L) return;
            long t = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            Tick.Total += t; Tick.Calls++;
            if (t > Tick.Worst) { Tick.Worst = t; Tick.WorstGc = System.GC.CollectionCount(0) != gcBefore; }
        }

        private static string Ms(long ticks) => (ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F3");

        internal static string Report()
        {
            string r = "main thread: room ticks " + Tick.Calls + " (" + Ms(Tick.Total) + " ms total, worst " + Ms(Tick.Worst) + " ms" + (Tick.WorstGc ? ", the garbage collector ran in it" : "") + "), zone scans " + Scan.Calls +
                " (worst " + Ms(Scan.Worst) + " ms), sound checks " + Sound.Calls + " (" + Ms(Sound.Total) + " ms total, worst " + Ms(Sound.Worst) + " ms)" +
                "; worst per part: measure " + Ms(Measure.Worst) + ", batch " + Ms(Batch.Worst) + ", rechecks " + Ms(Recheck.Worst) + ", following " + Ms(Follow.Worst) + ", directions " + Ms(Dirs.Worst) + ", waiting for ray jobs " + Ms(Rays.Worst) + ", budget " + Ms(Budget.Worst) + ", travel " + Ms(Travel.Worst) + ", the rest " + Ms(Rest.Worst) + " ms";
            Tick = Scan = Sound = Measure = Batch = Recheck = Follow = Dirs = Rays = Rest = Budget = Travel = default;
            return r;
        }
    }
}
