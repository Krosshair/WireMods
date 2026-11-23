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
        private static NativeArray<Vector4> s_dirs; // direction from the camera, distance from it
        private static NativeArray<Vector4> s_ears; // direction from the sound to your character's head, distance
        private static readonly List<AcousticFilter> List = new List<AcousticFilter>(256);
        private static readonly List<AcousticFilter> Changed = new List<AcousticFilter>(64);
        private static JobHandle s_job;
        private static bool s_pending;
        private const float SlowEvery = .25f;
        private static float s_slowNext;
        private static Vector3 s_jobEar; // your head when the job was scheduled: the sounds' positions are worked back from it

        private static NativeArray<Vector2> s_dop; // per sound: last distance (-1: none yet), smoothed speed away from you (Doppler)
        private static Vector3 s_lastEar;
        private static float s_lastAt = -1f;

        private struct Job : IJobParallelForTransform
        {
            public Vector3 Pos, Ear;
            public float Dt; // since the last job; 0: skip the speed (first job, a teleport)
            public NativeArray<Vector4> Dirs, Ears;
            public NativeArray<Vector2> Dop;

            public void Execute(int i, TransformAccess t)
            {
                if (!t.isValid) return;
                Vector3 p = t.position;
                // Store the source's actual world position. Applying the
                // previous frame's camera rotation here would make direct
                // and routed sounds use different temporal reference frames.
                // The main thread transforms it against the CURRENT camera.
                Dirs[i] = new Vector4(p.x, p.y, p.z, (p - Pos).magnitude);
                Vector3 e = Ear - p;
                float me = e.magnitude;
                Vector3 toEar = me > .01f ? e / me : Vector3.up;
                Ears[i] = new Vector4(toEar.x, toEar.y, toEar.z, me);
                // Doppler: how fast it's moving away from your head, smoothed over a few frames
                Vector2 dp = Dop[i];
                if (dp.x >= 0f && Dt > 1e-4f)
                {
                    float v = Mathf.Clamp((me - dp.x) / Dt, -Doppler.MaxSpeed, Doppler.MaxSpeed);
                    // At 60 Hz this matches the earlier .2 smoothing factor, but
                    // now its time constant is independent of frame rate.
                    const float tau = .07469f;
                    float response = 1f - Mathf.Exp(-Dt / tau);
                    dp.y += (v - dp.y) * response;
                }
                dp.x = me;
                Dop[i] = dp;
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
                // Wind carries sound: downwind of a sound it reaches further, upwind less (the wind
                // bends it down toward you or up over you). Grows with distance: nothing up close.
                // Only the direction (the camera turns every frame) needs every frame; the rest changes
                // slowly and glides in the filter anyway, so it's worked out 4 times a second. How far
                // and how loud are from your character's head (Hearing), the direction from the camera.
                float now = Time.time;
                bool slow = now >= s_slowNext;
                if (slow) s_slowNext = now + SlowEvery;
                Vector3 wind = Vector3.zero;
                if (slow && EnvMan.instance != null) wind = EnvMan.instance.GetWindDir() * EnvMan.instance.GetWindIntensity();
                for (int i = 0; i < List.Count; i++)
                {
                    AcousticFilter f = List[i];
                    if (f == null) continue;
                    Vector4 r = s_dirs[i], er = s_ears[i];
                    f.LastDist = er.w;
                    f.Doppler = f.Placed && Plugin.RealDistance.Value && Plugin.DopplerOn.Value ? Doppler.Speed / (Doppler.Speed + s_dop[i].y) : 1f;
                    if (f.Kind == AcousticFilter.Kinds.Other && (f.Doppler != 1f || f.OtherPitchKnown)) Doppler.ApplyOther(f);
                    Vector3 worldSource = new Vector3(r.x, r.y, r.z);
                    Vector3 direction = worldSource - lp;
                    float currentDist = direction.magnitude;
                    Vector3 local = currentDist > .01f ?
                        inv * (direction / currentDist) : Vector3.forward;
                    if (slow && f.Placed)
                    {
                        Vector3 toListener = new Vector3(er.x, er.y, er.z); // from the sound toward you
                        // measured: downwind a few dB louder, growing up to about 7 m/s of wind; upwind a
                        // shadow zone that takes 20 dB and more, full from about 4 m/s, formed by about
                        // 100 m (Waddington & Lam). The game's wind in m/s: its snow storm blows 1.3 to 2,
                        // a blizzard is 35 mph (15.6 m/s) and up (US National Weather Service), so 1 is 12 m/s.
                        // Upwind is held to 10 dB, not the measured 20: a choice for the game (not from a
                        // source), so a sound you can see in front of you doesn't vanish in a breeze.
                        float speed = 12f * Vector3.Dot(wind, toListener), ramp = Mathf.Clamp01((er.w - 15f) / 97f);
                        float carryDb = !Plugin.Wind.Value ? 0f : speed >= 0f ? 3f * Mathf.Clamp01(speed / 7f) * ramp : -10f * Mathf.Clamp01(-speed / 4f) * ramp;
                        f.DistanceGain = f.PastRange(er.w) * Mathf.Pow(10f, carryDb / 20f);
                        // Source shape and propagation are distinct:
                        // the former is handled when setting spatial width
                        // below, the latter is the path angular dispersion.
                    }
                    if (slow && f.Placed) f.AirCutoff = AcousticFilter.AirAt(er.w);
                    // a playing loop: its wall check again if you or it moved, or a building changed
                    if (f.WatchLoop)
                    {
                        Vector3 physicalSource = s_jobEar - new Vector3(er.x, er.y, er.z) * er.w;
                        ContinuousEdges.Evaluate(f, Hearing.Eye, physicalSource);
                        Occlusion.WatchLoop(f, physicalSource, Hearing.Eye, now);
                    }
                    if (f.Hrtf && f.Placed &&
                        (f.Kind == AcousticFilter.Kinds.Sfx ||
                         f.Kind == AcousticFilter.Kinds.Other ||
                         f.Kind == AcousticFilter.Kinds.Voice))
                    {
                        float physicalWidth = f.AreaRadius > 0f
                            ? Area.WidthAt(f.AreaRadius, currentDist) : 0f;
                        f.Width = Mathf.Max(f.BaseWidth,
                            Mathf.Max(physicalWidth, f.RouteSpread));
                    }
                    // Heard through an opening or round a corner: from there
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
            s_jobEar = Hearing.At;
            float t = Time.time;
            // a jump of your head (a teleport, a portal): no speed from it
            float dt = s_lastAt >= 0f && (Hearing.At - s_lastEar).sqrMagnitude < Doppler.Jump * Doppler.Jump ? t - s_lastAt : 0f;
            s_lastEar = Hearing.At; s_lastAt = t;
            s_job = new Job { Pos = listener.position, Ear = Hearing.At, Dt = dt,
                              Dirs = s_dirs, Ears = s_ears, Dop = s_dop }.ScheduleReadOnly(s_taa, 16); // read-only: never holds up the game moving things
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
                    Grow();
                    s_dop[f.Slot] = new Vector2(-1f, 0f); // no speed till it has a distance
                }
                else if (!want && f.Slot >= 0)
                {
                    int i = f.Slot, last = List.Count - 1;
                    s_taa.RemoveAtSwapBack(i);
                    AcousticFilter moved = List[last];
                    List[i] = moved;
                    if (!ReferenceEquals(moved, null)) moved.Slot = i;
                    s_dop[i] = s_dop[last]; // its Doppler state moves with it
                    List.RemoveAt(last);
                    f.Slot = -1;
                    f.Doppler = 1f;
                }
            }
            Changed.Clear();
        }

        private static void Grow()
        {
            if (s_dirs.IsCreated && s_dirs.Length >= List.Count) return;
            int cap = Mathf.NextPowerOfTwo(Mathf.Max(256, List.Count));
            var dop = new NativeArray<Vector2>(cap, Allocator.Persistent);
            if (s_dop.IsCreated) { NativeArray<Vector2>.Copy(s_dop, dop, s_dop.Length); s_dop.Dispose(); }
            s_dop = dop;
            if (s_dirs.IsCreated) { s_dirs.Dispose(); s_ears.Dispose(); }
            s_dirs = new NativeArray<Vector4>(cap, Allocator.Persistent);
            s_ears = new NativeArray<Vector4>(cap, Allocator.Persistent);
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
            if (s_dirs.IsCreated) { s_dirs.Dispose(); s_ears.Dispose(); }
            if (s_dop.IsCreated) s_dop.Dispose();
        }
    }
}
