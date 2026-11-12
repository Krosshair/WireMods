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
        internal static readonly float[] A1 = new float[Count], A2 = new float[Count], S1 = new float[Count], S2 = new float[Count];
        internal static readonly Vector3[] N1 = new Vector3[Count], N2 = new Vector3[Count];
        internal static readonly bool[] Has1 = new bool[Count], Has2 = new bool[Count];
        // Filled by Schedule, made live with the second bounces in Complete (so both layers match).
        private static readonly Vector3[] s_p1 = new Vector3[Count], s_r1 = new Vector3[Count];
        private static readonly float[] s_a1 = new float[Count], s_s1 = new float[Count];
        private static readonly Vector3[] s_n1 = new Vector3[Count];
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
                s_n1[i] = normal;
                s_s1[i] = Room.ScatterAt(i);
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
                    R1[i] = s_r1[i]; A1[i] = s_a1[i]; N1[i] = s_n1[i]; S1[i] = s_s1[i]; Has2[i] = false;
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
                Has1[i] = s_has1[i]; P1[i] = s_p1[i]; R1[i] = s_r1[i]; A1[i] = s_a1[i]; N1[i] = s_n1[i]; S1[i] = s_s1[i];
                Has2[i] = false;
            }
            for (int k = 0; k < s_cast; k++)
            {
                int i = Label[k];
                if (!Room.Surface(P1[i], R1[i], s_hits[k], MaxDistance, out Vector3 point, out Vector3 normal, out _, out float a, out _)) continue;
                P2[i] = point + normal * .1f;
                R2[i] = Vector3.Reflect(R1[i], normal);
                A2[i] = a;
                N2[i] = normal;
                S2[i] = Room.ScatterOf(s_hits[k].collider, a);
                Has2[i] = true;
            }
        }

        // Nothing to bounce off (switched off, or the measurement stopped).
        internal static void Clear()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            for (int i = 0; i < Count; i++) { Has1[i] = false; Has2[i] = false; }
        }

        // A way to the sound by one or two reflections. At each surface what isn't absorbed (1 - a)
        // goes on, split into a mirror-like part (1 - s) and a scattered part (s) (Vorlander and
        // Embrechts 2004), s being the surface's scattering coefficient (Room.ScatterOf). The scattered
        // part spreads by Lambert's cosine law (as ODEON models it, Zeng, Lynge and Rindel 2006). The
        // mirror-like part reaches the sound only if it lies within the cone that bounce point stands
        // for: each of the 64 room rays covers 4 pi / 64 of the sphere, a cone 14.4 degrees wide.
        internal struct Try
        {
            internal Vector3 Heard, End, Mirror, Normal;
            internal float Before;   // energy left before the last surface (after an earlier mirror bounce)
            internal float Absorb, Scatter, Length, Score;
        }

        private const float Patch = 4f * Mathf.PI / Count, PatchCos = 1f - Patch / (2f * Mathf.PI);

        // How loud this way arrives, against the sound straight from it (amplitude).
        internal static float Gain(Vector3 listener, Try t, Vector3 sound)
        {
            float direct = (sound - listener).magnitude;
            Vector3 leg = sound - t.End;
            float r2 = leg.magnitude;
            if (r2 < .01f) return 0f;
            Vector3 dir = leg / r2;
            float kept = t.Before * (1f - Mathf.Clamp01(t.Absorb));
            float s = Mathf.Clamp01(t.Scatter);
            float path = t.Length + r2;
            // mirror-like: as from the image, over the whole way
            float mirror = Vector3.Dot(t.Mirror, dir) >= PatchCos ? kept * (1f - s) * (direct * direct) / (path * path) : 0f;
            // scattered: the patch re-radiates with cos/pi; its area as seen from you is your ray's cone
            float cos = Mathf.Max(0f, Vector3.Dot(t.Normal, dir));
            float scattered = kept * s * (Patch / Mathf.PI) * cos * (direct * direct) / (r2 * r2);
            return Mathf.Min(1f, Mathf.Sqrt(mirror + scattered));
        }

        // The best few by how loud they arrive: up to Tries written, how many returned.
        internal static int Pick(Vector3 listener, Vector3 sound, Try[] into)
        {
            int got = 0;
            for (int i = 0; i < Count; i++)
            {
                if (!Has1[i]) continue;
                float l1 = (P1[i] - listener).magnitude;
                Consider(into, ref got, new Try { Heard = P1[i], End = P1[i], Mirror = R1[i], Normal = N1[i], Before = 1f, Absorb = A1[i], Scatter = S1[i], Length = l1 }, listener, sound);
                if (Has2[i]) // the first bounce went on as a mirror (the second ray follows its mirror direction)
                    Consider(into, ref got, new Try { Heard = P1[i], End = P2[i], Mirror = R2[i], Normal = N2[i], Before = (1f - Mathf.Clamp01(A1[i])) * (1f - Mathf.Clamp01(S1[i])), Absorb = A2[i], Scatter = S2[i], Length = l1 + (P2[i] - P1[i]).magnitude }, listener, sound);
            }
            return got;
        }

        private static void Consider(Try[] into, ref int got, Try t, Vector3 listener, Vector3 sound)
        {
            t.Score = Gain(listener, t, sound);
            if (t.Score <= 0f) return;
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
