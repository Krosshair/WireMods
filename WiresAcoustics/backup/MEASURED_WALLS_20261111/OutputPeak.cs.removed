using UnityEngine;

namespace WiresAcoustics
{
    // Debug log only: the loudest sample of what actually leaves the game (the listener's output)
    // and how many samples reached full scale (clipping, heard as a harsh, "deep fried" sound),
    // with the time of the last one, for each room report.
    internal static class OutputPeak
    {
        private static readonly float[] Buf = new float[2048];
        private static float s_peak;
        private static int s_clipped;
        private static float s_lastClip = -1f;

        internal static void Step()
        {
            if (!Plugin.DebugLog.Value) return;
            for (int ch = 0; ch < 2; ch++)
            {
                AudioListener.GetOutputData(Buf, ch);
                for (int i = 0; i < Buf.Length; i++)
                {
                    float a = Mathf.Abs(Buf[i]);
                    if (a > s_peak) s_peak = a;
                    if (a >= .999f) { s_clipped++; s_lastClip = Time.unscaledTime; }
                }
            }
        }

        internal static string Summary()
        {
            string r = "output peak " + (s_peak > 0f ? (20f * Mathf.Log10(s_peak)).ToString("F1") + " dBFS" : "none") +
                (s_clipped > 0 ? ", " + s_clipped + " samples at full scale (last at " + s_lastClip.ToString("F1") + " s)" : "");
            s_peak = 0f; s_clipped = 0;
            return r;
        }
    }
}
