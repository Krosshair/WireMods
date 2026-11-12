using System;

namespace WiresAcoustics
{
    // Simple main-thread counters. No per-frame logger or allocations.
    // Emitted only with the existing three-second room debug report.
    internal static class RoomStats
    {
        internal static int Scheduled, Skipped, Invalidated;
        internal static string Summary()
        {
            string report = "room rays scheduled " + Scheduled + ", stationary skips " + Skipped
                + ", geometry invalidations " + Invalidated;
            Scheduled = Skipped = Invalidated = 0;
            return report;
        }
    }
}
