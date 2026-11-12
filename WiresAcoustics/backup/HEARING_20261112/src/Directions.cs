using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

namespace WiresAcoustics
{
    // Where each sound is relative to your head (the HRTF) and how far (distance fade, air, wind): read
    // by a Unity job on worker threads (transform access), scheduled each frame and picked up the next.
    // The main thread copies the direction into the filters every frame, the rest 4 times a second.
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
        private const float SlowEvery = .25f;
        private static float s_slowNext;

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
                Quaternion rot = listener.rotation, inv = Quaternion.Inverse(rot);
                // Wind carries sound: downwind of a sound it reaches further, upwind less (the wind
                // bends it down toward you or up over you). Grows with distance: nothing up close.
                Doppler.Frame(lp);
                float dt = Time.deltaTime;
                // Only the direction (your head turns every frame) and the Doppler shift (a pitch that
                // has to glide) need every frame; the rest changes slowly and glides in the filter
                // anyway, so it's worked out 4 times a second.
                float now = Time.time;
                bool slow = now >= s_slowNext;
                if (slow) s_slowNext = now + SlowEvery;
                Vector3 wind = Vector3.zero;
                if (slow && EnvMan.instance != null) wind = EnvMan.instance.GetWindDir() * EnvMan.instance.GetWindIntensity();
                for (int i = 0; i < List.Count; i++)
                {
                    AcousticFilter f = List[i];
                    if (f == null) continue;
                    Vector4 r = s_dirs[i];
                    f.LastDist = r.w;
                    if (f.Placed) { Doppler.Update(f, r.w, dt); if (f.Kind == AcousticFilter.Kinds.Other) Doppler.ApplyOther(f); }
                    Vector3 local = new Vector3(r.x, r.y, r.z);
                    if (slow && f.Placed)
                    {
                        Vector3 toListener = -(rot * local); // from the sound toward you
                        // measured: downwind a few dB louder, growing up to about 7 m/s of wind; upwind a
                        // shadow zone that takes 20 dB and more, full from about 4 m/s, formed by about
                        // 100 m (Waddington & Lam). The game's wind in m/s: its snow storm blows 1.3 to 2,
                        // a blizzard is 35 mph (15.6 m/s) and up (US National Weather Service), so 1 is 12 m/s.
                        // Upwind is held to 10 dB, not the measured 20: a choice for the game (not from a
                        // source), so a sound you can see in front of you doesn't vanish in a breeze.
                        float speed = 12f * Vector3.Dot(wind, toListener), ramp = Mathf.Clamp01((r.w - 15f) / 97f);
                        float carryDb = speed >= 0f ? 3f * Mathf.Clamp01(speed / 7f) * ramp : -10f * Mathf.Clamp01(-speed / 4f) * ramp;
                        f.DistanceGain = f.FadeAt(r.w) * Mathf.Pow(10f, carryDb / 20f);
                        // an area (a hearth, lava) sounds as wide as it looks from here
                        if (f.AreaRadius > 0f) f.Width = Mathf.Max(f.BaseWidth, Area.WidthAt(f.AreaRadius, r.w));
                    }
                    if (slow && f.Placed) f.AirCutoff = AcousticFilter.AirAt(r.w);
                    // heard through an opening, round a corner or by a bounce: from there
                    if (f.Hrtf && f.Kind != AcousticFilter.Kinds.Ambient)
                    {
                        Vector3 heard = f.Routed ? f.RoutePoint : Vector3.zero;
                        Vector3 toHeard = heard - lp;
                        f.SetDirection(heard != Vector3.zero && toHeard.sqrMagnitude > 1e-4f ? inv * toHeard.normalized : local);
                    }
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
