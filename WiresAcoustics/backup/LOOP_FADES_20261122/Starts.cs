using System.Collections.Generic;
using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // New sounds and the travel of sound. A sound that starts doesn't play at once: its wall check
    // (three rays to it and three to the side of its source you'd see) goes to the worker threads,
    // and the next frame it starts already muffled as it should be. A random bird or insect call
    // first has its open-air spot found the same way (rays down from above), then its wall check.
    // Then it plays as late as its distance makes it at 350 m/s (Doppler.Speed), the frame or two it
    // waited counted in: up close the wait is the travel, further off it's hidden inside it.
    // Your own sounds (nothing between your head and your body) start at once.
    internal static class Starts
    {
        private const int MaxHits = Occlusion.MaxHits;
        private const float Most = .5f; // longest travel delay (a one-off's object is removed after its clip; this keeps its end)

        private struct Req
        {
            internal ZSFX Z;
            internal AudioSource Src;
            internal Vector3 Eye, To, Face;
            internal bool HasFace, Air;
            internal Vector3 Away; // Air: the way out from you
            internal float Asked;
            internal int First, Generation, MoveRetries, VolumeN;
        }
        private static readonly List<Req> Queue = new List<Req>(32);
        private static Req[] s_flight = new Req[32];
        private static Vector3[] s_volumePoints = new Vector3[32 * SourceVolume.MaxSamples];
        private static int s_flightCount;
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending;
        private static int s_geometryRetries, s_motionRetries, s_volumeRays;
        internal static string Summary()
        {
            string report = "new-sound rechecks geometry " + s_geometryRetries +
                ", large movement " + s_motionRetries +
                ", initial volume rays " + s_volumeRays;
            s_geometryRetries = s_motionRetries = s_volumeRays = 0;
            return report;
        }
        private static readonly RaycastHit[] Down = new RaycastHit[MaxHits];

        private static ZSFX s_hold; // the sound whose start is being held (UpdateReverb inside its Play, then Play's end)

        // Valheim chooses a single nearest looping source when a prefab's
        // m_maxConcurrentSources == 0 (AudioMan.UpdateLoopingConcurrency
        // compares the loop index against zero). The losing AudioSources
        // still play but ZSFX fades their volume to zero. Each real-world
        // emitter must instead play at its own position and phase.
        private static bool IndependentWorldLoop(ZSFX z)
        {
            if (!Plugin.Enabled.Value || z == null || !z.IsLooping()) return false;
            AudioSource source = z.GetComponent<AudioSource>();
            if (source == null || source.spatialBlend < .5f) return false;
            AcousticFilter f = z.GetComponent<AcousticFilter>();
            return f == null || !(f.Cue || f.Voice || f.WorldRandomAmbient ||
                f.Kind == AcousticFilter.Kinds.Ambient ||
                f.Kind == AcousticFilter.Kinds.Outside ||
                f.Kind == AcousticFilter.Kinds.Menu);
        }

        private static void AllowWorldLoop(ZSFX z)
        {
            if (IndependentWorldLoop(z)) z.ConcurrencyEnable();
        }

        // Patch the actual caller, not the tiny ConcurrencyDisable setter:
        // the latter can be inlined by Unity's Mono JIT and evade Harmony.
        // No additional update loop: vanilla already runs this selection
        // every 16 frames. Its 2D/ambient concurrency remains untouched.
        [HarmonyPatch(typeof(AudioMan), "UpdateLoopingConcurrency")]
        private static class KeepWorldLoopsAudible
        {
            private static void Postfix(List<ZSFX> ___m_loopingSfx)
            {
                if (!Plugin.Enabled.Value || Time.frameCount % 16 != 0 ||
                    ___m_loopingSfx == null) return;
                foreach (ZSFX z in ___m_loopingSfx) AllowWorldLoop(z);
            }
        }

        // From Occlusion.Run, inside the sound's Play: check its walls first, start it after.
        internal static void Hold(ZSFX z, AudioSource src, Vector3 to, Vector3? face)
        {
            Queue.Add(new Req { Z = z, Src = src, Eye = Hearing.At, To = to, Face = face ?? to, HasFace = face.HasValue, Asked = Time.time });
            s_hold = z;
        }

        // A random call: its open-air spot first (six rays down, Occlusion.InOpenAir's), then its walls.
        internal static void HoldAir(ZSFX z, AudioSource src)
        {
            Vector3 away = z.transform.position - Hearing.At; away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            Queue.Add(new Req { Z = z, Src = src, To = z.transform.position, Air = true, Away = away, Asked = Time.time });
            s_hold = z;
        }

        // The end of the game's Play: a held sound is stopped before it's heard; any other one-off
        // placed sound starts as late as its distance makes it.
        [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
        private static class PlayEnd
        {
            private static void Postfix(ZSFX __instance, AudioSource ___m_audioSource)
            {
                AudioSource src = ___m_audioSource;
                if (src == null) return;
                // Undo initial suppression from AudioMan.RegisterLoopingSound
                // before the loop begins (the periodic selection also keeps
                // all positional emitters enabled thereafter).
                AllowWorldLoop(__instance);
                if (ReferenceEquals(s_hold, __instance)) { s_hold = null; src.Stop(); return; }
                if (!Plugin.Enabled.Value || !src.isPlaying) return;
                Travel(__instance, src, 0f);
            }
        }

        // Give every independently playing world loop a different sample
        // starting position. This avoids synchronized, repeated recordings;
        // it does NOT replace the separate vanilla concurrency fix above.
        // Apply to normal and deferred Play paths.
        private static void RandomizeLoop(ZSFX z, AudioSource src)
        {
            if (!Plugin.RandomLoopPhase.Value || !src.loop ||
                src.spatialBlend < .5f || src.clip == null ||
                src.clip.length < .75f || src.clip.samples < 64) return;
            AcousticFilter f = z.GetComponent<AcousticFilter>();
            if (f != null && (f.Cue || f.Voice || f.WorldRandomAmbient ||
                              f.Kind == AcousticFilter.Kinds.Ambient ||
                              f.Kind == AcousticFilter.Kinds.Outside ||
                              f.Kind == AcousticFilter.Kinds.Menu)) return;
            src.timeSamples = UnityEngine.Random.Range(0, src.clip.samples);
        }

        // Held back by its distance from your character's head, less what it already waited.
        private static void Travel(ZSFX z, AudioSource src, float waited)
        {
            if (src.loop)
            {
                if (!src.isPlaying) src.Play();
                RandomizeLoop(z, src);
                return;
            }
            if (src.spatialBlend < .5f) { if (!src.isPlaying) src.Play(); return; }
            AcousticFilter f = z.GetComponent<AcousticFilter>();
            float delay = (f != null && f.Cue) || !Plugin.TravelTime.Value ? 0f : Mathf.Min((z.transform.position - Hearing.At).magnitude / Doppler.Speed, Most) - waited;
            if (delay < .001f) { if (!src.isPlaying) src.Play(); return; } // under a millisecond: now
            if (src.isPlaying) src.Stop();
            src.PlayDelayed(delay);
        }

        // Every frame from the game's audio update, first: last frame's checks read and their sounds
        // started, then (Schedule, last) this frame's sent off.
        internal static void Step()
        {
            if (!s_pending) return;
            s_job.Complete(); // a handful of rays, done by now
            s_pending = false;
            float now = Time.time;
            for (int i = 0; i < s_flightCount; i++)
            {
                Req r = s_flight[i];
                s_flight[i].Z = null; s_flight[i].Src = null;
                if (r.Z == null || r.Src == null) continue;
                // A wall can be placed, a door closed or the source moved
                // while the initial sound rays are executing. Do not start
                // audio using a geometry result from the old world state.
                Vector3 current = r.Z.transform.position;
                bool geometryChanged = r.Generation != Occlusion.WorldGen &&
                    Occlusion.ChangedNear(r.Generation,
                        r.Air ? r.To : r.Eye, r.To,
                        Corners.Reach + Occlusion.SameWall);
                bool listenerMoved = !r.Air &&
                    (r.Eye - Hearing.At).sqrMagnitude > .25f * .25f;
                bool sourceMoved = !r.Air &&
                    (r.To - current).sqrMagnitude > .25f * .25f;
                // Allow one extra check for rapid movement, then start it:
                // perpetual movement must never starve a moving sound.
                if (geometryChanged || ((listenerMoved || sourceMoved) && r.MoveRetries < 1))
                {
                    if (geometryChanged) s_geometryRetries++;
                    else { s_motionRetries++; r.MoveRetries++; }
                    r.Eye = Hearing.At;
                    r.To = current;
                    if (sourceMoved || listenerMoved) r.HasFace = false;
                    Queue.Add(r); // resubmitted with the current scene, still silent
                    continue;
                }
                if (r.Air)
                {
                    PlaceInOpenAir(r);
                    if (!Plugin.Occlusion.Value) { Occlusion.Resume(r.Z, r.Src, 0f, Occlusion.Block.Clear); Travel(r.Z, r.Src, now - r.Asked); continue; }
                    // its walls from the new spot, next
                    r.Air = false;
                    r.To = r.Z.transform.position;
                    r.Eye = Hearing.At;
                    r.HasFace = false;
                    Queue.Add(r);
                    continue;
                }
                AcousticFilter f = r.Z.GetComponent<AcousticFilter>();
                Occlusion.Own = f != null && f.ShapeRoot != null ? f.ShapeRoot.root : null;
                Occlusion.Block block = Occlusion.Block.Clear;
                float occ = Batch.DirectWalls(s_cmds, s_hits, r.First, r.To,
                    ref block, out float gain, out float cutoff,
                    out float coverage);
                if (r.VolumeN > 0)
                {
                    float lowEnergy = gain * gain;
                    float highEnergy = SpectralPaths.HighEnergy(gain, cutoff);
                    float covered = coverage, obstruction = occ;
                    for (int v = 0; v < r.VolumeN; v++)
                    {
                        Occlusion.Block part = Occlusion.Block.Clear;
                        float localOcc = Batch.VolumeRay(s_cmds, s_hits,
                            r.First + 6 + v,
                            s_volumePoints[i * SourceVolume.MaxSamples + v],
                            ref part, out float g, out float cut);
                        lowEnergy += g * g;
                        highEnergy += SpectralPaths.HighEnergy(g, cut);
                        covered += part.Walls > 0 || part.Terrain ||
                                   part.Soft > 0f || part.Through < 1f ||
                                   part.BendGain < 1f ? 1f : 0f;
                        obstruction += localOcc;
                        if (!block.HasWall && part.HasWall) block = part;
                        else if (block.FirstBox == null && part.FirstBox != null)
                            block.FirstBox = part.FirstBox;
                    }
                    float inv = 1f / (r.VolumeN + 1f);
                    SpectralPaths.Decode(lowEnergy * inv, highEnergy * inv,
                        out gain, out cutoff);
                    coverage = covered * inv;
                    occ = obstruction * inv;
                }
                // A visible face is a SECOND emitting region, not a binary
                // exemption that erases all obstruction at the sound pivot.
                if (r.HasFace)
                {
                    Occlusion.Block face = Occlusion.Block.Clear;
                    float faceOcc = Batch.DirectWalls(s_cmds, s_hits,
                        r.First + 3, r.Face, ref face,
                        out float faceGain, out float faceCut,
                        out float faceCoverage);
                    ThreeRayTransmission.Blend(gain, cutoff, faceGain, faceCut,
                        .5f, out gain, out cutoff);
                    occ = .5f * (occ + faceOcc);
                    coverage = .5f * (coverage + faceCoverage);
                    // Preserve a real wall for subsequent edge probing.
                    if (!block.HasWall && face.HasWall) block = face;
                }
                Occlusion.Own = null;
                if (f != null) f.LastVolumeSamples = r.VolumeN;
                Occlusion.Resume(r.Z, r.Src, occ, block,
                                 gain, cutoff, coverage);
                // First-play sources use the same continuous wall coverage
                // as playing loops, rather than switching on the first
                // follow-up propagation batch. No additional scene casts.
                if (block.Walls <= 1 && !block.Terrain &&
                    block.Soft <= 0f && block.FirstBox != null)
                {
                    f = r.Z.GetComponent<AcousticFilter>();
                    if (f != null)
                    {
                        AnalyticOcclusion.Bind(f,
                            block.FirstBox.C as BoxCollider, block.FirstBox, r.To);
                        if (AnalyticOcclusion.Evaluate(f, r.Eye, r.To,
                            out float firstGain, out float firstCut,
                            out float firstCovered))
                        {
                            f.AnalyticActive = true;
                            f.AnalyticIndirectGain = 0f;
                            f.LastCoverage = firstCovered;
                            f.LastOcc = firstCovered;
                            f.AcousticPath = "start/analytic-box";
                            float bend = Mathf.Clamp01(f.AnalyticBendShare);
                            f.Routed = bend > .001f;
                            f.RoutePoint = Vector3.Lerp(r.To,f.AnalyticEdgePoint,bend);
                            Underwater.Apply(ref firstGain, ref firstCut,
                                             f.UnderAmount);
                            f.Set(firstGain, firstCut, true);
                        }
                    }
                }
                Travel(r.Z, r.Src, now - r.Asked);
            }
            s_flightCount = 0;
        }

        // Every frame from the game's audio update, last: this frame's new sounds' rays to the workers.
        internal static void Schedule()
        {
            if (s_pending || Queue.Count == 0) return;
            int n = Queue.Count, cmds = 6 * n;
            for (int i = 0; i < n; i++)
            {
                Req check = Queue[i];
                AcousticFilter f = !check.Air && check.Z != null ?
                    check.Z.GetComponent<AcousticFilter>() : null;
                check.VolumeN = !check.Air && Plugin.Occlusion.Value ?
                    SourceVolume.Count(f, check.Eye, check.To,
                        Plugin.VolumetricRays.Value) : 0;
                cmds += check.VolumeN;
                Queue[i] = check;
            }
            RayJobs.Ensure(ref s_cmds, ref s_hits, cmds, MaxHits);
            if (s_flight.Length < n)
            {
                int size = Mathf.NextPowerOfTwo(n);
                System.Array.Resize(ref s_flight, size);
                System.Array.Resize(ref s_volumePoints, size * SourceVolume.MaxSamples);
            }
            var q = RayJobs.Query(Room.Mask);
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                Req r = Queue[i];
                r.First = c;
                r.Generation = Occlusion.WorldGen;
                if (r.Air)
                {
                    Vector3 p = r.To;
                    for (int s = 0; s < 6; s++, p += r.Away * Occlusion.CallStep)
                        s_cmds[c++] = new RaycastCommand(new Vector3(p.x, p.y + 150f, p.z), Vector3.down, q, 300f);
                }
                else
                {
                    Cast3(r.Eye, r.To, q, ref c);
                    Cast3(r.Eye, r.HasFace ? r.Face : r.To, q, ref c);
                    AcousticFilter volume = r.VolumeN > 0 && r.Z != null ?
                        r.Z.GetComponent<AcousticFilter>() : null;
                    SourceVolume.Projection projection = r.VolumeN > 0 ?
                        SourceVolume.Project(volume, r.Eye, r.To) : default;
                    s_volumeRays += r.VolumeN;
                    for (int v = 0; v < r.VolumeN; v++)
                    {
                        Vector3 point = SourceVolume.Point(in projection,
                            r.To, v, r.VolumeN);
                        s_volumePoints[i * SourceVolume.MaxSamples + v] = point;
                        s_cmds[c++] = Batch.Cast(r.Eye, point, q);
                    }
                }
                s_flight[i] = r;
            }
            s_flightCount = n;
            Queue.Clear();
            s_job = RayJobs.Submit(s_cmds.GetSubArray(0, c),
                s_hits.GetSubArray(0, c * MaxHits), 4, MaxHits);
            s_pending = true;
        }

        private static void Cast3(Vector3 from, Vector3 to, QueryParameters q, ref int c) =>
            RayJobs.Cast3(s_cmds, from, to, q, ref c);

        // Occlusion.InOpenAir's choice, from the rays' hits: on top of whatever is there, 1 m up, not
        // on a building right by you.
        private static void PlaceInOpenAir(Req r)
        {
            Transform t = r.Z.transform;
            Vector3 p = r.To, last = p;
            bool any = false;
            for (int s = 0; s < 6; s++, p += r.Away * Occlusion.CallStep)
            {
                int cmd = r.First + s;
                int n = RayJobs.ReadHits(s_hits, cmd, MaxHits, Down);
                float highest = float.MinValue;
                int top = -1;
                for (int i = 0; i < n; i++)
                    if (Down[i].point.y > highest) { highest = Down[i].point.y; top = i; }
                // An empty sample says nothing about the next candidate:
                // the first column may be over a steep drop or a void while
                // another of the six samples has usable terrain.
                if (top < 0) continue;
                float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : highest;
                last = new Vector3(p.x, Mathf.Max(highest, sea) + Occlusion.CallHeight, p.z);
                any = true;
                Vector3 flat = p - Hearing.At; flat.y = 0f;
                if (flat.sqrMagnitude < Occlusion.NotOver * Occlusion.NotOver && (Solids.Of(Down[top])?.Built ?? false)) continue;
                t.position = last;
                return;
            }
            if (any) t.position = last;
        }

        // Switched off: what's waiting starts now as the game would.
        internal static void Flush()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            for (int i = 0; i < s_flightCount; i++) { Req r = s_flight[i]; if (r.Src != null && !r.Src.isPlaying) r.Src.Play(); s_flight[i].Z = null; s_flight[i].Src = null; }
            foreach (Req r in Queue) if (r.Src != null && !r.Src.isPlaying) r.Src.Play();
            s_flightCount = 0;
            Queue.Clear();
            s_hold = null;
        }

        internal static void Shutdown()
        {
            Flush();
            RayJobs.Dispose(ref s_cmds, ref s_hits);
        }
    }
}
