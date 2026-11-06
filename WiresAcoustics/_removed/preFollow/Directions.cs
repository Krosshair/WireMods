using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

namespace WiresAcoustics
{
    // Where each sound is relative to your head, for the HRTF, and how far (the distance fade): read by
    // a Unity job on worker threads (transform access), scheduled each frame and picked up the next.
    // The main thread only copies the results into the filters. The list of sounds is rebuilt only
    // when it changes.
    internal static class Directions
    {
        private static TransformAccessArray s_taa;
        private static NativeArray<Vector4> s_dirs; // direction, distance
        private static AcousticFilter[] s_snap = new AcousticFilter[64];
        private static int s_count;
        private static JobHandle s_job;
        private static bool s_pending;
        internal static bool Dirty = true;

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

        // Every frame, from the game's audio update.
        internal static void Step(Transform listener)
        {
            if (s_pending)
            {
                s_job.Complete();
                s_pending = false;
                Vector3 lp = listener.position;
                Quaternion inv = Quaternion.Inverse(listener.rotation);
                for (int i = 0; i < s_count; i++)
                {
                    AcousticFilter f = s_snap[i];
                    if (f == null) continue;
                    Vector4 r = s_dirs[i];
                    if (f.Fades) f.DistanceGain = f.FadeAt(r.w);
                    // heard through an opening or round a corner: from there
                    if (f.Hrtf) f.SetDirection(f.Routed ? inv * (f.RoutePoint - lp).normalized : new Vector3(r.x, r.y, r.z));
                }
            }
            if (Dirty) Rebuild();
            if (s_count == 0) return;
            s_job = new Job { Pos = listener.position, InvRot = Quaternion.Inverse(listener.rotation), Dirs = s_dirs }.ScheduleReadOnly(s_taa, 16); // read-only: never holds up the game moving things
            JobHandle.ScheduleBatchedJobs();
            s_pending = true;
        }

        private static void Rebuild()
        {
            Dirty = false;
            int n = 0;
            foreach (AcousticFilter f in AcousticFilter.Live) if (f != null && (f.Hrtf || f.Fades)) n++;
            if (s_snap.Length < n) s_snap = new AcousticFilter[Mathf.NextPowerOfTwo(n)];
            var ts = new Transform[n];
            int k = 0;
            foreach (AcousticFilter f in AcousticFilter.Live)
                if (f != null && (f.Hrtf || f.Fades) && k < n) { s_snap[k] = f; ts[k++] = f.transform; }
            for (int i = k; i < s_count; i++) s_snap[i] = null;
            s_count = k;
            if (!s_taa.isCreated) s_taa = new TransformAccessArray(ts);
            else s_taa.SetTransforms(ts);
            if (!s_dirs.IsCreated || s_dirs.Length < Mathf.Max(1, k))
            {
                if (s_dirs.IsCreated) s_dirs.Dispose();
                s_dirs = new NativeArray<Vector4>(Mathf.NextPowerOfTwo(Mathf.Max(64, k)), Allocator.Persistent);
            }
        }

        // Pending directions dropped (HRTF switched off); the list is rebuilt next frame.
        internal static void Flush()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            for (int i = 0; i < s_count; i++) s_snap[i] = null;
            s_count = 0;
            Dirty = true;
        }

        internal static void Shutdown()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            if (s_taa.isCreated) s_taa.Dispose();
            if (s_dirs.IsCreated) s_dirs.Dispose();
            s_count = 0;
        }
    }
}
