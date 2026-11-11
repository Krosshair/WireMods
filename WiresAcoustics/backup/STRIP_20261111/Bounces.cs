using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // Sound reaching you by bouncing, as in Vercidium's ray traced audio.
    //
    // Each room ray that hit something (Room, 64 directions) bounces once more: mirrored off the
    // surface it hit (by its normal: the angle it came in at is the angle it leaves at) and cast again
    // on worker threads, after the room measurement. That gives two layers of points the listener
    // reaches by reflection: first bounces (seen directly) and second bounces (one reflection away).
    //
    // Paths: a sound with walls in the way (after its direct check) also tries the three bounce points
    // with the shortest way to it: listener -> first bounce (-> second bounce) -> sound. Its cost is
    // the walls on the last leg, what each surface swallows (its absorption) and how far the last leg
    // is from the mirror direction (a reflection is strongest where the angle in equals the angle out,
    // weaker the further off). Down a corridor and round its far corner, or round two corners, that
    // finds the way the openings and corner points can't. Heard from the first bounce.
    //
    internal static class Bounces
    {
        internal const int Count = 64, Tries = 3;
        internal static readonly Vector3[] P1 = new Vector3[Count], P2 = new Vector3[Count], R1 = new Vector3[Count], R2 = new Vector3[Count];
        internal static readonly float[] A1 = new float[Count], A2 = new float[Count];
        internal static readonly bool[] Has1 = new bool[Count], Has2 = new bool[Count];
        // Filled by Schedule, made live with the second bounces in Complete (so both layers match).
        private static readonly Vector3[] s_p1 = new Vector3[Count], s_r1 = new Vector3[Count];
        private static readonly float[] s_a1 = new float[Count];
        private static readonly bool[] s_has1 = new bool[Count];
        private static readonly int[] Label = new int[Count];
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending;
        private static int s_cast;
        private const float MaxDistance = 50f;
        private static float s_secondNeededUntil;
        private static int s_secondUsed, s_secondSkipped;
        // A blocked, actively playing sound needs routes. Clear rooms and
        // ambient-only environments don't need 64 secondary bounce rays.
        internal static void Wake()
        {
            bool newlyNeeded = Time.time >= s_secondNeededUntil;
            s_secondNeededUntil = Mathf.Max(s_secondNeededUntil, Time.time + 1.5f);
            // Make the first SECOND-bounce geometry available quickly for
            // a newly obstructed one-shot, even when the listener stands
            // still and the 1-second room sampling throttle is active.
            if (newlyNeeded) Room.Invalidate();
        }

        // From Room.Measure, with its hits: the first bounces, and the second bounce rays scheduled.
        internal static void Schedule(Vector3[] dirs)
        {
            if (s_pending) { s_job.Complete(); s_pending = false; } // not read in time: replaced
            bool second = Plugin.Occlusion.Value && Time.time < s_secondNeededUntil;
            if (second && !s_cmds.IsCreated)
            {
                s_cmds = new NativeArray<RaycastCommand>(Count, Allocator.Persistent);
                s_hits = new NativeArray<RaycastHit>(Count, Allocator.Persistent);
            }
            var q = new QueryParameters(Room.Mask, false, QueryTriggerInteraction.Ignore, false);
            int n = 0;
            for (int i = 0; i < Count && i < dirs.Length; i++)
            {
                s_has1[i] = Room.Hit[i];
                if (!s_has1[i]) continue;
                float a = Room.Alpha[i];
                Vector3 normal = Room.Normal[i];
                s_p1[i] = Room.Point[i] + normal * .1f;
                s_r1[i] = Vector3.Reflect(dirs[i], normal);
                s_a1[i] = a;
                if (second)
                {
                    s_cmds[n] = new RaycastCommand(s_p1[i], s_r1[i], q, MaxDistance);
                    Label[n] = i;
                    n++;
                }

            }
            s_cast = n;
            if (!second)
            {
                // First reflection still exists and can be chosen even when
                // no second cast is needed. Keep both layers synchronized.
                for (int i = 0; i < Count; i++)
                {
                    Has1[i] = s_has1[i]; P1[i] = s_p1[i];
                    R1[i] = s_r1[i]; A1[i] = s_a1[i]; Has2[i] = false;
                }
                s_secondSkipped++;
            }
            else s_secondUsed++;
            if (n > 0)
            {
                s_job = RaycastCommand.ScheduleBatch(s_cmds.GetSubArray(0, n), s_hits.GetSubArray(0, n), 16, 1);
                JobHandle.ScheduleBatchedJobs();
                s_pending = true;
            }

        }

        // Second bounces read back and both layers made live (from Room, before the next measurement).
        internal static void Complete()
        {
            if (!s_pending) return;
            s_job.Complete();
            s_pending = false;
            for (int i = 0; i < Count; i++)
            {
                Has1[i] = s_has1[i]; P1[i] = s_p1[i]; R1[i] = s_r1[i]; A1[i] = s_a1[i];
                Has2[i] = false;
            }
            for (int k = 0; k < s_cast; k++)
            {
                int i = Label[k];
                if (!Room.Surface(P1[i], R1[i], s_hits[k], MaxDistance, out Vector3 point, out Vector3 normal, out _, out float a, out _)) continue;
                P2[i] = point + normal * .1f;
                R2[i] = Vector3.Reflect(R1[i], normal);
                A2[i] = a;
                Has2[i] = true;
            }
        }

        // Nothing to bounce off (switched off, or the measurement stopped).
        internal static void Clear()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            for (int i = 0; i < Count; i++) { Has1[i] = false; Has2[i] = false; }
        }

        internal struct Try { internal Vector3 Heard, End, Mirror; internal float Cost, Length, Score; }

        // The best few bounce paths to a sound by length: up to Tries written, how many returned.
        internal static int Pick(Vector3 listener, Vector3 sound, Try[] into)
        {
            int got = 0;
            for (int i = 0; i < Count; i++)
            {
                if (!Has1[i]) continue;
                float l1 = (P1[i] - listener).magnitude;
                Consider(into, ref got, new Try { Heard = P1[i], End = P1[i], Mirror = R1[i], Cost = 1.5f * A1[i], Length = l1 }, sound);
                if (Has2[i])
                    Consider(into, ref got, new Try { Heard = P1[i], End = P2[i], Mirror = R2[i], Cost = 1.5f * (A1[i] + A2[i]), Length = l1 + (P2[i] - P1[i]).magnitude }, sound);
            }
            return got;
        }

        private static void Consider(Try[] into, ref int got, Try t, Vector3 sound)
        {
            Vector3 leg = sound - t.End;
            float total = t.Length + leg.magnitude;
            float direction = leg.sqrMagnitude > .001f ? Vector3.Dot(t.Mirror, leg.normalized) : 1f;
            float turn = .5f * (1f - direction);
            // Reflections are strongest on hard surfaces and near specular
            // direction. A shorter path across an absorbing rug should not
            // outrank a slightly longer path off stone.
            t.Score = 1f / (Mathf.Max(1f, total) * (1f + t.Cost + turn));
            int slot = -1;
            if (got < Tries) slot = got++;
            else
            {
                float weakest = float.MaxValue;
                for (int k = 0; k < Tries; k++)
                {
                    float score = into[k].Score;
                    if (score < weakest) { weakest = score; slot = k; }
                }
                if (t.Score <= weakest) return;
            }
            into[slot] = t;
        }

        // A tried path's cost besides the walls on its last leg: absorption, and how far the last leg
        // turns off the mirror direction (0 on it, 0.5 walls at 90 degrees, 1 turning straight back).
        internal static float Cost(Try t, Vector3 sound) =>
            t.Cost + .5f * (1f - Vector3.Dot(t.Mirror, (sound - t.End).normalized));

        internal static float LengthGain(Vector3 listener, Try t, Vector3 sound)
        {
            float direct = (sound - listener).magnitude;
            float path = t.Length + (sound - t.End).magnitude;
            return Mathf.Min(1f, direct / Mathf.Max(.01f, path)); // the longer way spreads the sound further (1/r)
        }

        internal static string Summary()
        {
            int a = 0, b = 0;
            for (int i = 0; i < Count; i++) { if (Has1[i]) a++; if (Has2[i]) b++; }
            string report = "bounces " + a + "+" + b +
                " (second rays enabled " + s_secondUsed + " room checks, skipped " + s_secondSkipped + ")";
            s_secondUsed = s_secondSkipped = 0;
            return report;
        }

        internal static void Shutdown()
        {
            Clear();
            if (s_cmds.IsCreated) { s_cmds.Dispose(); s_hits.Dispose(); }
        }
    }
}
