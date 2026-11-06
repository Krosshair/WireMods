namespace WiresAcoustics
{
    // Main-thread time this mod takes (Debug log only): total and worst single call per report.
    internal static class Cost
    {
        internal struct Stat { internal long Total, Worst; internal int Calls; }
        internal static Stat Tick, Scan, Sound;

        internal static void Add(ref Stat s, long start)
        {
            long t = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            s.Total += t; s.Calls++;
            if (t > s.Worst) s.Worst = t;
        }

        private static string Ms(long ticks) => (ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F3");

        internal static string Report()
        {
            string r = "main thread: room ticks " + Tick.Calls + " (" + Ms(Tick.Total) + " ms total, worst " + Ms(Tick.Worst) + " ms), zone scans " + Scan.Calls +
                " (worst " + Ms(Scan.Worst) + " ms), sound checks " + Sound.Calls + " (" + Ms(Sound.Total) + " ms total, worst " + Ms(Sound.Worst) + " ms)";
            Tick = Scan = Sound = default;
            return r;
        }
    }
}
