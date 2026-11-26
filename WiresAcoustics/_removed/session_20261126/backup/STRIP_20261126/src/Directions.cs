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

        private static NativeArray<Vector4> s_reflDelay, s_reflLevel; // per sound and wall (Reflections)
        private static NativeArray<Vector2> s_dop; // per sound: last distance (-1: none yet), smoothed speed away from you (Doppler)
        private static Vector3 s_lastEar;
        private static float s_lastAt = -1f;

        private struct Job : IJobParallelForTransform
        {
            public Vector3 Pos, Ear;
            public float Dt; // since the last job; 0: skip the speed (first job, a teleport)
            public NativeArray<Vector4> Dirs, Ears;
            public NativeArray<Vector2> Dop;
            public Vector4 W0, W1, W2, W3, Back, Scale; // the walls (Reflections)
            public NativeArray<Vector4> ReflDelay, ReflLevel;

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
                Reflections.Image(p, Ear, me, W0, W1, W2, W3, Back, Scale, out Vector4 rd, out Vector4 rl);
                ReflDelay[i] = rd; ReflLevel[i] = rl;
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
            AnalyticWorker.CompleteReady();
            if (s_pending)
            {
                // Transform-access jobs are asynchronous. If physics or
                // other workers are busy, keep the previous spatial state
                // instead of forcing a main-thread job completion stall.
                if (!s_job.IsCompleted) return;
                s_job.Complete();
                s_pending = false;
                Vector3 lp = listener.position;
                Quaternion inv = Quaternion.Inverse(listener.rotation);
                Reflections.CameraInverse = inv;
                float reflStrength = Plugin.ReverbStrength.Value;
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
                    if (Reflections.On && f.Hrtf && f.Placed)
                    {
                        if (f.ReflHistory == null) f.ReflHistory = new float[Reflections.History];
                        Vector4 rd = s_reflDelay[i], rl = s_reflLevel[i];
                        float[] want = f.ReflTarget;
                        for (int k = 0; k < Reflections.Walls; k++) { want[k] = rd[k]; want[Reflections.Walls + k] = rl[k] * reflStrength; }
                    }
                    else if (f.ReflHistory != null) System.Array.Clear(f.ReflTarget, Reflections.Walls, Reflections.Walls);
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
                        // Approximate Valheim's normalized wind strength as
                        // 0..12 m/s, projected along the source-listener path.
                        // The measured 2.5 and 5 m/s FHWA ground-level
                        // propagation curves are interpolated, NOT linearly
                        // extrapolated above the strongest studied wind.
                        // Listener ground material and height are already
                        // derived from the existing Room probes.
                        float speed = 12f * Vector3.Dot(wind, toListener);
                        float exposure = Plugin.Occlusion.Value ? Ambience.WindExposure : 1f;
                        float carryDb = WindCarryDb(speed, er.w, exposure,
                            Room.GroundSoftness, Room.GroundReceiverHeight,
                            Plugin.Wind.Value);
                        f.DistanceGain = f.PastRange(er.w) * Mathf.Pow(10f, carryDb / 20f);
                        // Source shape and propagation are distinct:
                        // the former is handled when setting spatial width
                        // below, the latter is the path angular dispersion.
                    }
                    if (slow && f.Placed)
                    {
                        f.SetAir(er.w);
                        Ground(f, worldSource);
                    }
                    // a playing loop: its wall check again if you or it moved, or a building changed
                    if (f.WatchLoop || f.AnalyticActive)
                    {
                        Vector3 physicalSource = s_jobEar - new Vector3(er.x, er.y, er.z) * er.w;
                        if (f.WatchLoop)
                        {
                            ContinuousEdges.Evaluate(f, Hearing.At, physicalSource);
                            Occlusion.WatchLoop(f, physicalSource, Hearing.At, now);
                        }
                        // This also covers moving, longer one-shot effects.
                        if (f.AnalyticActive)
                            AnalyticWorker.Request(f, Hearing.At, physicalSource);
                    }
                    if (f.Hrtf && f.Placed &&
                        (f.Kind == AcousticFilter.Kinds.Sfx ||
                         f.Kind == AcousticFilter.Kinds.Other ||
                         f.Kind == AcousticFilter.Kinds.Voice))
                    {
                        float physicalWidth = !f.LocalizedEvent && f.AreaRadius > 0f
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
                AnalyticWorker.Submit();
            }
            Apply();
            if (List.Count == 0) return;
            s_jobEar = Hearing.At;
            float t = Time.time;
            // a jump of your head (a teleport, a portal): no speed from it
            float dt = s_lastAt >= 0f && (Hearing.At - s_lastEar).sqrMagnitude < Doppler.Jump * Doppler.Jump ? t - s_lastAt : 0f;
            s_lastEar = Hearing.At; s_lastAt = t;
            s_job = new Job { Pos = listener.position, Ear = Hearing.At, Dt = dt,
                              Dirs = s_dirs, Ears = s_ears, Dop = s_dop,
                              W0 = Reflections.Planes[0], W1 = Reflections.Planes[1], W2 = Reflections.Planes[2], W3 = Reflections.Planes[3],
                              Back = new Vector4(Reflections.Back[0], Reflections.Back[1], Reflections.Back[2], Reflections.Back[3]),
                              Scale = new Vector4(Reflections.Scale[0], Reflections.Scale[1], Reflections.Scale[2], Reflections.Scale[3]),
                              ReflDelay = s_reflDelay, ReflLevel = s_reflLevel }.ScheduleReadOnly(s_taa, 16); // read-only: never holds up the game moving things
            JobHandle.ScheduleBatchedJobs();
            s_pending = true;
        }

        // The 0.6.0 +3/-10 fixed caps are replaced with ground/height/
        // distance/wind-dependent FHWA measurements (WindPropagation).
        // Room exposure still attenuates the wind effect while sheltered.
        // All per-source work remains inside the existing 4 Hz spatial pass.
        internal static float WindCarryDb(float speed, float distance,
            float exposure, float groundSoftness, float receiverHeight, bool windEnabled)
        {
            return windEnabled ? WindPropagation.Correction(speed, distance,
                exposure, groundSoftness, receiverHeight) : 0f;
        }

        // The ground between a sound and you (ISO 9613-2, table 3): over porous ground the wave
        // bouncing off it cancels part of the direct one at 250 to 500 Hz, more the farther and the
        // lower the two are (6 to 8 dB at 30 m over grass). Heard against how it is at the sound's
        // full-volume distance, where the game set its level, so hard ground changes nothing and
        // nothing changes near a sound. The ground is taken as flat at the height under you, of the
        // kind round you (Room). Fitted as one dip (a peaking filter, Q 0.7: on average within
        // 0.7 dB of the table's bands, at most 3.4; ISO itself states 3 dB).
        private static readonly float[] GroundBands = { 125f, 250f, 500f, 1000f };
        private static readonly float[] s_ground = new float[4], s_groundNear = new float[4];

        private static void Ground(AcousticFilter f, Vector3 source)
        {
            if (!Plugin.RealDistance.Value || f.Source == null) { f.GroundDipDb = 0f; return; }
            float hr = Room.GroundReceiverHeight, G = Room.GroundPorous;
            float hs = Mathf.Max(0f, source.y - (Hearing.At.y - hr));
            Vector3 flat = source - Hearing.At; flat.y = 0f;
            float dp = flat.magnitude, near = Mathf.Max(1f, f.Source.minDistance);
            if (dp <= near || G <= .01f) { f.GroundDipDb = 0f; return; }
            GroundTable(hs, hr, dp, G, s_ground);
            GroundTable(hs, hr, near, G, s_groundNear);
            // the band dips against near the sound; centre where they're deepest
            float deepest = 0f, weight = 0f, logSum = 0f;
            for (int i = 0; i < 4; i++)
            {
                float dip = s_ground[i] - s_groundNear[i];
                if (dip <= 0f) continue;
                if (dip > deepest) deepest = dip;
                weight += dip * dip;
                logSum += dip * dip * Mathf.Log(GroundBands[i]);
            }
            f.GroundDipDb = deepest;
            if (weight > 0f) f.GroundDipHz = Mathf.Exp(logSum / weight);
        }

        // As + Ar at 125, 250, 500 and 1000 Hz (dB); the middle region only touches hard ground and
        // the bands above 1 kHz are the same at every distance.
        private static void GroundTable(float hs, float hr, float dp, float G, float[] o)
        {
            for (int i = 0; i < 4; i++) o[i] = 0f;
            for (int side = 0; side < 2; side++)
            {
                float h = side == 0 ? hs : hr, near = 1f - Mathf.Exp(-dp / 50f), h2 = h * h;
                o[0] += G * (1.5f + 3f * Mathf.Exp(-.12f * (h - 5f) * (h - 5f)) * near + 5.7f * Mathf.Exp(-.09f * h2) * (1f - Mathf.Exp(-2.8e-6f * dp * dp)));
                o[1] += G * (1.5f + 8.6f * Mathf.Exp(-.09f * h2) * near);
                o[2] += G * (1.5f + 14f * Mathf.Exp(-.46f * h2) * near);
                o[3] += G * (1.5f + 5f * Mathf.Exp(-.9f * h2) * near);
            }
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
            if (s_dirs.IsCreated) { s_dirs.Dispose(); s_ears.Dispose(); s_reflDelay.Dispose(); s_reflLevel.Dispose(); }
            s_dirs = new NativeArray<Vector4>(cap, Allocator.Persistent);
            s_ears = new NativeArray<Vector4>(cap, Allocator.Persistent);
            s_reflDelay = new NativeArray<Vector4>(cap, Allocator.Persistent);
            s_reflLevel = new NativeArray<Vector4>(cap, Allocator.Persistent);
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
            AnalyticWorker.Flush();
        }

        internal static void Shutdown()
        {
            Flush();
            foreach (AcousticFilter f in List) if (!ReferenceEquals(f, null)) f.Slot = -1;
            foreach (AcousticFilter f in Changed) if (!ReferenceEquals(f, null)) f.Queued = false;
            List.Clear();
            Changed.Clear();
            if (s_taa.isCreated) s_taa.Dispose();
            if (s_dirs.IsCreated) { s_dirs.Dispose(); s_ears.Dispose(); s_reflDelay.Dispose(); s_reflLevel.Dispose(); }
            if (s_dop.IsCreated) s_dop.Dispose();
        }
    }
}
