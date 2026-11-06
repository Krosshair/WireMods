using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

namespace WiresAcoustics
{
    // Where each sound is relative to your head (the HRTF) and how far (distance fade, air): read by a
    // Unity job on worker threads (transform access), scheduled each frame and picked up the next.
    // The main thread only copies the results into the filters.
    //
    // The list of sounds changes in place: a sound starting or ending is one add or one swap-remove,
    // applied between jobs (sounds start and end many times a second, so the list is never rebuilt).
    internal static class Directions
    {
        private static TransformAccessArray s_taa;
        private static NativeArray<Vector4> s_dirs; // direction, distance
        private static readonly List<AcousticFilter> List = new List<AcousticFilter>(256);
        private static readonly List<AcousticFilter> Changed = new List<AcousticFilter>(64);
        private static JobHandle s_job;
        private static bool s_pending;

        private struct Job : IJobParallelForTransform
        {
            public Vector3 Pos;
            public Quaternion InvRot;
            public NativeArray<Vector4> Dirs;

            public void Execute(int i, TransformAccess t)
            {
                if (!t.isValid) return;
                Vector3 d = t.position - Pos;
                float m = d.magnitude;
                Vector3 dir = m > .01f ? InvRot * (d / m) : Vector3.forward;
                Dirs[i] = new Vector4(dir.x, dir.y, dir.z, m);
            }
        }

        // From the filter, whenever it may need tracking or no longer does (applied at the next Step).
        internal static void Want(AcousticFilter f)
        {
            if (f.Queued) return;
            f.Queued = true;
            Changed.Add(f);
        }

        // Every frame, from the game's audio update.
        internal static void Step(Transform listener)
        {
            if (s_pending)
            {
                s_job.Complete();
                s_pending = false;
                Vector3 lp = listener.position;
                Quaternion inv = Quaternion.Inverse(listener.rotation);
                for (int i = 0; i < List.Count; i++)
                {
                    AcousticFilter f = List[i];
                    if (f == null) continue;
                    Vector4 r = s_dirs[i];
                    f.LastDist = r.w;
                    if (f.Fades) f.DistanceGain = f.FadeAt(r.w);
                    if (f.Placed) f.AirCutoff = AcousticFilter.AirAt(r.w);
                    // heard through an opening, round a corner or by a bounce: from there
                    if (f.Hrtf && f.Kind != AcousticFilter.Kinds.Ambient)
                        f.SetDirection(f.Routed ? inv * (f.RoutePoint - lp).normalized : new Vector3(r.x, r.y, r.z));
                }
            }
            Apply();
            if (List.Count == 0) return;
            s_job = new Job { Pos = listener.position, InvRot = Quaternion.Inverse(listener.rotation), Dirs = s_dirs }.ScheduleReadOnly(s_taa, 16); // read-only: never holds up the game moving things
            JobHandle.ScheduleBatchedJobs();
            s_pending = true;
        }

        private static void Apply()
        {
            if (Changed.Count == 0) return;
            if (!s_taa.isCreated) s_taa = new TransformAccessArray(256);
            foreach (AcousticFilter f in Changed)
            {
                if (ReferenceEquals(f, null)) continue;
                f.Queued = false;
                bool want = f != null && f.Tracked;
                if (want && f.Slot < 0)
                {
                    f.Slot = List.Count;
                    List.Add(f);
                    s_taa.Add(f.transform);
                }
                else if (!want && f.Slot >= 0)
                {
                    int i = f.Slot, last = List.Count - 1;
                    s_taa.RemoveAtSwapBack(i);
                    AcousticFilter moved = List[last];
                    List[i] = moved;
                    if (!ReferenceEquals(moved, null)) moved.Slot = i;
                    List.RemoveAt(last);
                    f.Slot = -1;
                }
            }
            Changed.Clear();
            if (!s_dirs.IsCreated || s_dirs.Length < List.Count)
            {
                if (s_dirs.IsCreated) s_dirs.Dispose();
                s_dirs = new NativeArray<Vector4>(Mathf.NextPowerOfTwo(Mathf.Max(256, List.Count)), Allocator.Persistent);
            }
        }

        // The mod switched off: no jobs, but sounds coming and going still update the list.
        internal static void Idle()
        {
            Flush();
            Apply();
        }

        // Pending directions dropped (HRTF switched off).
        internal static void Flush()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
        }

        internal static void Shutdown()
        {
            Flush();
            foreach (AcousticFilter f in List) if (!ReferenceEquals(f, null)) f.Slot = -1;
            foreach (AcousticFilter f in Changed) if (!ReferenceEquals(f, null)) f.Queued = false;
            List.Clear();
            Changed.Clear();
            if (s_taa.isCreated) s_taa.Dispose();
            if (s_dirs.IsCreated) s_dirs.Dispose();
        }
    }
}
