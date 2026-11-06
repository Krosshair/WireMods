using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // Sound reaching you by bouncing, as in Vercidium's ray traced audio, and echoes.
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
    // Echo: out in the open, a big surface facing you 15 to 50 m away (a cliff, a hillside, a wall)
    // sends sounds back from its side. The strongest is picked from the room rays (facing you, hard,
    // near, several rays on it). Each one-off sound gets a duller copy from that direction, as late
    // as its own way there and back is longer than its way to you, and as much quieter (Directions,
    // AcousticFilter); a loop doesn't (a steady sound's echo is just the same sound). Fades out the
    // more enclosed you are, where the room's reverb is what you hear.
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
        private static readonly int[] Label = new int[Count * 2];
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending;
        private static int s_cast;
        private const float MaxDistance = 50f;

        // Echoes: up to three surfaces, world direction, distance and level (smoothed); EchoGain is the
        // strongest. Room's tick hands them to the filters.
        internal const int Taps = AcousticFilter.Taps;
        internal static readonly Vector3[] EchoDirs = { Vector3.forward, Vector3.forward, Vector3.forward };
        internal static readonly float[] EchoDists = { 20f, 20f, 20f }, EchoGains = new float[Taps];
        internal static float EchoGain;
        // Past the room rays' 50 m: open directions are followed on to 230 m (a valley's far side, a
        // cliff across the water), as far as the echoes' delay lines reach.
        private const float Far = 230f, FarFrom = 49f;
        private static readonly bool[] s_farHit = new bool[Count];
        private static readonly float[] s_farDist = new float[Count], s_farA = new float[Count];
        private static readonly Vector3[] s_farNormal = new Vector3[Count];
        private static Vector3[] s_dirs;
        private static float s_covered;
        private static Vector3 s_listenerAt;

        // From Room.Measure, with its hits: the first bounces, and the second bounce rays scheduled.
        internal static void Schedule(Vector3[] dirs, float covered, Vector3 from)
        {
            if (s_pending) { s_job.Complete(); s_pending = false; } // not read in time: replaced
            if (!s_cmds.IsCreated)
            {
                s_cmds = new NativeArray<RaycastCommand>(Count * 2, Allocator.Persistent);
                s_hits = new NativeArray<RaycastHit>(Count * 2, Allocator.Persistent);
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
                s_cmds[n] = new RaycastCommand(s_p1[i], s_r1[i], q, MaxDistance);
                Label[n] = i;
                n++;
            }
            // open directions, followed on past the room rays
            for (int i = 0; i < Count && i < dirs.Length; i++)
            {
                s_farHit[i] = false;
                if (Room.Hit[i] || dirs[i].y < -.25f) continue;
                s_cmds[n] = new RaycastCommand(from + dirs[i] * FarFrom, dirs[i], q, Far - FarFrom);
                Label[n] = Count + i;
                n++;
            }
            s_dirs = dirs;
            s_listenerAt = from;
            s_covered = covered;
            s_cast = n;
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
                if (i >= Count)
                {
                    i -= Count;
                    Vector3 o = s_listenerAt + s_dirs[i] * FarFrom;
                    if (Room.Surface(o, s_dirs[i], s_hits[k], Far - FarFrom, out _, out Vector3 fn, out float fd, out float fa, out bool tree) && !tree)
                    {
                        s_farHit[i] = true; s_farDist[i] = FarFrom + fd; s_farA[i] = fa; s_farNormal[i] = fn;
                    }
                    continue;
                }
                if (!Room.Surface(P1[i], R1[i], s_hits[k], MaxDistance, out Vector3 point, out Vector3 normal, out _, out float a, out _)) continue;
                P2[i] = point + normal * .1f;
                R2[i] = Vector3.Reflect(R1[i], normal);
                A2[i] = a;
                Has2[i] = true;
            }
            PickEchoes();
        }

        // The echoing surfaces: facing you, at least 15 m off (nearer, an echo isn't heard apart from
        // the sound), not the ground under you, not trunks; a bare rock face sends back nearly all, a
        // snow slope little (its absorption). Up to three, in different directions, strongest first by
        // facing x (1 - absorption) / distance. How big each is: how many rays found it, and a ray's
        // footprint grows with distance (one ray at 200 m covers a hillside, at 20 m a doorway: one
        // stray ray close by isn't a cliff). Kept in the same slot from one measurement to the next
        // while it's the same surface, so they glide instead of jumping.
        private static readonly Vector3[] s_candDir = new Vector3[Count * 2];
        private static readonly float[] s_candDist = new float[Count * 2], s_candA = new float[Count * 2], s_candScore = new float[Count * 2];
        private static readonly float[] s_target = new float[Taps];
        private static readonly Vector3[] s_targetDir = new Vector3[Taps];
        private static readonly float[] s_targetDist = new float[Taps];
        private static void PickEchoes()
        {
            if (s_dirs == null) return;
            int c = 0;
            for (int i = 0; i < Count && i < s_dirs.Length; i++)
            {
                Vector3 d = s_dirs[i];
                if (d.y < -.25f) continue;
                if (Room.Hit[i] && !Room.Tree[i] && Room.Dist[i] >= 15f) Add(ref c, d, Room.Normal[i], Room.Dist[i], Room.Alpha[i]);
                else if (s_farHit[i]) Add(ref c, d, s_farNormal[i], s_farDist[i], s_farA[i]);
            }
            for (int k = 0; k < Taps; k++) s_target[k] = 0f;
            bool[] used = s_used;
            for (int i = 0; i < c; i++) used[i] = false;
            for (int pick = 0; pick < Taps; pick++)
            {
                int best = -1;
                for (int i = 0; i < c; i++)
                {
                    if (used[i]) continue;
                    bool apart = true;
                    for (int k = 0; k < pick; k++) if (Vector3.Dot(s_candDir[i], s_pickDir[k]) > .87f) { apart = false; break; }
                    if (apart && (best < 0 || s_candScore[i] > s_candScore[best])) best = i;
                }
                if (best < 0) break;
                used[best] = true;
                Vector3 dir = s_candDir[best];
                float dist = s_candDist[best];
                int rays = 0;
                for (int i = 0; i < c; i++) if (Vector3.Dot(s_candDir[i], dir) > .9f) rays++;
                float size = Mathf.Clamp01((rays * Mathf.Max(1f, dist / 40f) - 1f) / 3f);
                // echoes fade out under a roof, where the room's reverb is what you hear; a valley or a ravine
                // is open above, however closed in its sides make it
                float level = size * (1f - s_candA[best]) * Mathf.Clamp01(1.4f - 2f * s_covered);
                s_pickDir[pick] = dir;
                if (level <= .005f) continue;
                // the slot already on this surface, else the quietest
                int slot = -1;
                for (int k = 0; k < Taps; k++) if (EchoGains[k] > .005f && s_target[k] <= 0f && Vector3.Dot(EchoDirs[k], dir) > .9f) { slot = k; break; }
                if (slot < 0)
                    for (int k = 0; k < Taps; k++) if (s_target[k] <= 0f && (slot < 0 || EchoGains[k] < EchoGains[slot])) slot = k;
                if (slot < 0) break;
                s_target[slot] = level; s_targetDir[slot] = dir; s_targetDist[slot] = dist;
            }
            EchoGain = 0f;
            for (int k = 0; k < Taps; k++)
            {
                if (s_target[k] > .005f)
                {
                    bool same = EchoGains[k] > .005f && Vector3.Dot(EchoDirs[k], s_targetDir[k]) > .9f;
                    if (same)
                    {
                        EchoDirs[k] = Vector3.Slerp(EchoDirs[k], s_targetDir[k], .5f).normalized;
                        EchoDists[k] = Mathf.Lerp(EchoDists[k], s_targetDist[k], .5f);
                    }
                    else if (EchoGains[k] <= .005f) { EchoDirs[k] = s_targetDir[k]; EchoDists[k] = s_targetDist[k]; }
                    else { EchoGains[k] = Mathf.Lerp(EchoGains[k], 0f, .35f); EchoGain = Mathf.Max(EchoGain, EchoGains[k]); continue; } // fade the old one out first
                }
                EchoGains[k] = Mathf.Lerp(EchoGains[k], s_target[k], .35f);
                if (EchoGains[k] < .003f && s_target[k] <= 0f) EchoGains[k] = 0f;
                EchoGain = Mathf.Max(EchoGain, EchoGains[k]);
            }
        }

        private static readonly bool[] s_used = new bool[Count * 2];
        private static readonly Vector3[] s_pickDir = new Vector3[Taps];

        private static void Add(ref int c, Vector3 dir, Vector3 normal, float dist, float a)
        {
            float cos = Vector3.Dot(-dir, normal);
            if (cos <= .7f) return;
            s_candDir[c] = dir; s_candDist[c] = dist; s_candA[c] = a;
            s_candScore[c] = cos * (1f - a) / dist;
            c++;
        }

        // Nothing to bounce off (switched off, or the measurement stopped).
        internal static void Clear()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            for (int i = 0; i < Count; i++) { Has1[i] = false; Has2[i] = false; }
            EchoGain = 0f;
            for (int k = 0; k < Taps; k++) EchoGains[k] = 0f;
        }

        internal struct Try { internal Vector3 Heard, End, Mirror; internal float Cost, Length; }

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
            float total = t.Length + (sound - t.End).magnitude;
            int slot = -1;
            if (got < Tries) slot = got++;
            else
            {
                float worst = 0f;
                for (int k = 0; k < Tries; k++)
                {
                    float w = into[k].Length + (sound - into[k].End).magnitude;
                    if (w > worst) { worst = w; slot = k; }
                }
                if (total >= worst) return;
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
            return Mathf.Clamp(direct / Mathf.Max(.01f, path), .25f, 1f);
        }

        internal static string Summary()
        {
            int a = 0, b = 0;
            for (int i = 0; i < Count; i++) { if (Has1[i]) a++; if (Has2[i]) b++; }
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < Taps; k++) if (EchoGains[k] > .003f) sb.Append((sb.Length > 0 ? ", " : "") + EchoGains[k].ToString("F2") + " from " + EchoDists[k].ToString("F0") + " m");
            return "bounces " + a + "+" + b + ", echo " + (sb.Length > 0 ? sb.ToString() : "none");
        }

        internal static void Shutdown()
        {
            Clear();
            if (s_cmds.IsCreated) { s_cmds.Dispose(); s_hits.Dispose(); }
        }
    }
}
