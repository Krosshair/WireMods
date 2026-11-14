using System.Collections.Generic;
using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // The space around the listener, measured with rays (Unity's batched raycasts, run on worker
    // threads: scheduled on one tick, read on the next). 64 directions, first hit each:
    //  - enclosure: share of level and upward rays that hit something (open sky and open doorways
    //    let sound out);
    //  - mean free path: average hit distance, the room's size as sound sees it;
    //  - absorption: by what was hit (building material, soil, rock), escaped rays absorb fully.
    // Decay time from Sabine's formula with mean free path l = 4V/S: RT60 = 0.161 V / (S a) = 0.04 l / a.
    // Drives one reverb zone that follows the listener; the game's own zones are removed while this
    // runs. Outdoors is measured the same way: open sky lets the sound out (dry), the ground is
    // what it is where each ray lands (snow, grass, forest floor, stone, water), and trunks round
    // you give a forest its own soft tail.
    internal static class Room
    {
        private const int Rays = 64;
        private const float MaxDistance = 50f, Interval = .2f, Smooth = .35f, RescanEvery = 30f; // a safety net: locations and dungeons are handled as they're placed (Spawned)
        private static readonly Vector3[] Dirs = BuildDirs();
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending, s_haveCurrent;
        private static float s_next, s_nextScan, s_nextLog, s_nextVoice, s_nextRecheck;
        private static Vector3 s_from;
        private static int s_mask;
        private static AudioReverbZone s_zone;
        private static Transform s_zoneParent, s_listener;
        private static Params s_current, s_applied;
        private static bool s_haveApplied;
        // Save original Unity reverb zones rather than destroying them.
        // Required for toggling acoustics OFF without reloading the area.
        private struct VanillaZone { internal AudioReverbZone Zone; internal bool Enabled; }
        private static readonly List<VanillaZone> VanillaZones = new List<VanillaZone>(48);
        private static bool s_roomDirty = true, s_haveCast, s_restoreComplete;
        private static Vector3 s_lastCastPosition;
        private static float s_lastCastTime;
        private const float StationaryResample = 1f;
        internal static void Invalidate() { s_roomDirty = true; RoomStats.Invalidated++; }


        // Measured, smoothed (read by Occlusion for the per-sound reverb send).
        internal static float Enclosure;
        // What the walls round you let through of the outside, as the first wall a sound comes through
        // (amplitude; 0 in the open, where there are none). Ambience's muffled bed.
        internal static float WallBed;
        // Where a sound is as much reverb as direct (the critical distance, Hopkins-Stryker as Arup
        // gives it): D = 0.057 sqrt(Q V / RT), Q = 1 for a sound sending all round, V the space's
        // volume (from the room rays, each ray's cone out to what it hit), RT the reverb time.
        internal static float Critical = 5f;

        // Judgement values (no source), so Valheim's rooms sound like the real rooms they stand for:
        // they're built at about twice real size (a warm basement room of 2 x 6 x 2 m becomes 8 x 12 x 2
        // in the game), and real homes are full of soft things (rugs, sofas, curtains, clothes) that
        // the game's rooms mostly lack. Only the reverb's sound uses these; how much reverb each sound
        // gets, and everything else, uses the room as built.
        private const float RoomScale = .5f, LivedIn = .1f;
        private static float s_path, s_alpha, s_decay;
        // Valheim's environment-loop mixer can take 10 seconds to fade the old
        // track out, then another 10 to fade the new one in. Speed that handoff
        // ONLY around measured indoor/outdoor acoustic transitions, then restore
        // the original fade duration for ordinary weather/biome changes.
        private static AudioMan s_fastAmbientMan;
        private static float s_fastAmbientOriginal, s_fastAmbientUntil;
        private const float BoundaryAmbientFade = .25f;

        private static void SpeedAmbientAtBoundary()
        {
            AudioMan man = AudioMan.instance;
            if (man == null) return;
            if (s_fastAmbientMan != man)
            {
                RestoreAmbientFade();
                s_fastAmbientMan = man;
                s_fastAmbientOriginal = man.m_ambientFadeTime;
            }
            s_fastAmbientUntil = Time.time + 3f;
            man.m_ambientFadeTime = Mathf.Min(s_fastAmbientOriginal, BoundaryAmbientFade);
        }

        private static void RestoreAmbientFade()
        {
            if (s_fastAmbientMan != null &&
                Mathf.Abs(s_fastAmbientMan.m_ambientFadeTime - Mathf.Min(s_fastAmbientOriginal, BoundaryAmbientFade)) < .001f)
                s_fastAmbientMan.m_ambientFadeTime = s_fastAmbientOriginal;
            s_fastAmbientMan = null;
            s_fastAmbientUntil = 0f;
        }

        internal static int Mask
        {
            get
            {
                if (s_mask == 0) s_mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain", "vehicle");
                return s_mask;
            }
        }

        private static Vector3[] BuildDirs()
        {
            var d = new Vector3[Rays];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < Rays; i++)
            {
                float y = 1f - (i + .5f) / Rays * 2f, r = Mathf.Sqrt(1f - y * y), t = golden * i;
                d[i] = new Vector3(Mathf.Cos(t) * r, y, Mathf.Sin(t) * r);
            }
            return d;
        }

        private struct Params
        {
            internal float Room, RoomHF, Decay, DecayHF, Reflections, ReflectionsDelay, Reverb, ReverbDelay;
            internal static Params Lerp(Params a, Params b, float t) => new Params
            {
                Room = Mathf.Lerp(a.Room, b.Room, t), RoomHF = Mathf.Lerp(a.RoomHF, b.RoomHF, t), Decay = Mathf.Lerp(a.Decay, b.Decay, t),
                DecayHF = Mathf.Lerp(a.DecayHF, b.DecayHF, t), Reflections = Mathf.Lerp(a.Reflections, b.Reflections, t),
                ReflectionsDelay = Mathf.Lerp(a.ReflectionsDelay, b.ReflectionsDelay, t), Reverb = Mathf.Lerp(a.Reverb, b.Reverb, t),
                ReverbDelay = Mathf.Lerp(a.ReverbDelay, b.ReverbDelay, t)
            };

            internal void ApplyTo(AudioReverbZone z)
            {
                z.room = (int)Mathf.Clamp(Room, -10000f, 0f);
                z.roomHF = (int)Mathf.Clamp(RoomHF, -10000f, 0f);
                z.decayTime = Mathf.Clamp(Decay, .1f, 20f);
                z.decayHFRatio = Mathf.Clamp(DecayHF, .1f, 2f);
                z.reflections = (int)Mathf.Clamp(Reflections, -10000f, 1000f);
                z.reflectionsDelay = Mathf.Clamp(ReflectionsDelay, 0f, .3f);
                z.reverb = (int)Mathf.Clamp(Reverb, -10000f, 2000f);
                z.reverbDelay = Mathf.Clamp(ReverbDelay, 0f, .1f);
            }

            internal bool Differs(Params o) =>
                Mathf.Abs(Room - o.Room) > 15f || Mathf.Abs(RoomHF - o.RoomHF) > 25f || Mathf.Abs(Decay - o.Decay) > .03f ||
                Mathf.Abs(DecayHF - o.DecayHF) > .02f || Mathf.Abs(Reflections - o.Reflections) > 25f ||
                Mathf.Abs(ReflectionsDelay - o.ReflectionsDelay) > .002f || Mathf.Abs(Reverb - o.Reverb) > 25f ||
                Mathf.Abs(ReverbDelay - o.ReverbDelay) > .002f;

            public override string ToString() =>
                "room " + Room.ToString("F0") + " hf " + RoomHF.ToString("F0") + " decay " + Decay.ToString("F2") + "s hfRatio " + DecayHF.ToString("F2") +
                " refl " + Reflections.ToString("F0") + "/" + (ReflectionsDelay * 1000f).ToString("F0") + "ms late " + Reverb.ToString("F0") + "/" + (ReverbDelay * 1000f).ToString("F0") + "ms";
        }

        // Riding on the game's own per-frame audio update, a few times a second.
        [HarmonyPatch(typeof(AudioMan), "Update")]
        private static class Tick
        {
            private static bool s_wasOn, s_wasHrtf, s_wasRoomReverb;

            private static void Postfix(AudioMan __instance)
            {
                long t0 = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                int gc0 = t0 != 0L ? System.GC.CollectionCount(0) : 0;
                // Switched off while playing: hand every live sound back to the game, finish pending work.
                bool on = Plugin.Enabled.Value, hrtf = on && Plugin.Hrtf.Value;
                bool roomReverb = on && Plugin.RoomReverb.Value;
                // Re-enabling the mod or room reverb must immediately
                // suppress the newly restored native zones. Otherwise
                // vanilla + custom reverb could overlap for 30 seconds.
                if (roomReverb && !s_wasRoomReverb) { s_nextScan = 0f; Room.Invalidate(); }
                s_wasRoomReverb = roomReverb;
                if (s_wasHrtf && !hrtf) { Directions.Flush(); AcousticFilter.ReleaseHrtf(); }
                if (s_wasOn && !on) { PortalLoop.Clear(); Batch.Flush(); AcousticFilter.ReleaseAll(); PlayerSounds.RestoreAll(); Ambience.RestorePlaced(); Underwater.Reset(); Follow.Clear(); }
                s_wasOn = on; s_wasHrtf = hrtf;
                if (!on) { Underwater.Reset(); Directions.Idle(); }
                if (Plugin.Enabled.Value && __instance.GetActiveAudioListener() is AudioListener l)
                {
                    Transform lt = l.transform;
                    // a new listener (the menu's, then the game camera's after loading in) comes with the
                    // game's own reverb zone: removed at once, not at the next safety check
                    if (lt != s_listener) { s_listener = lt; s_nextScan = 0f; s_roomDirty = true; s_haveCast = false; }
                    // everything not timed on its own adds up as "the rest"
                    long r = Cost.Start(), rest = 0L;
                    Hearing.Update(lt);
                    Underwater.Update(Hearing.At, Time.deltaTime);
                    AcousticFilter.Strength = Mathf.Clamp(Plugin.HrtfStrength.Value, 0f, 2f);
                    long c = Cost.Start();
                    if (r != 0L) rest += c - r;
                    Follow.Step();
                    PortalLoop.Step(Hearing.Camera);
                    Cost.Add(ref Cost.Follow, c);
                    c = Cost.Start();
                    Directions.Step(Hearing.Camera);
                    Cost.Add(ref Cost.Dirs, c);
                    r = Cost.Start();
                    Ambience.Pan(__instance, lt, Time.deltaTime);
                    float t = Time.time;
                    GeometryEvents.Step(t); // building changes held while a base loads in
                    c = Cost.Start();
                    if (r != 0L) rest += c - r;
                    if (t >= s_nextVoice) { s_nextVoice = t + Interval; Occlusion.Voices(Hearing.At); }
                    if (t >= s_nextRecheck) { s_nextRecheck = t + .5f; Occlusion.Recheck(Hearing.At); }
                    Cost.Add(ref Cost.Recheck, c);
                    if (r != 0L) Cost.AddTicks(ref Cost.Rest, rest);
                    c = Cost.Start();
                    Budget.Step(t);
                    Cost.Add(ref Cost.Budget, c);
                    c = Cost.Start();
                    Batch.Step(Hearing.At);
                    Cost.Add(ref Cost.Batch, c);
                    if (t >= s_nextScan)
                    {
                        s_nextScan = t + RescanEvery;
                        long t1 = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                        RemoveVanilla();
                        if (t1 != 0L) Cost.Add(ref Cost.Scan, t1);
                    }
                }
                Run(__instance);
                Cost.AddTick(t0, gc0);
            }

            private static void Run(AudioMan __instance)
            {
                WriteLog();
                // The rays measure the space for the reverb and for muffling ambience: on if either is.
                if (!Plugin.Enabled.Value || (!Plugin.RoomReverb.Value && !Plugin.Occlusion.Value))
                {
                    // Restore exactly ONCE. Repeating it on every audio
                    // update was querying 3 AudioSources and allocating a
                    // tiny array every frame even while disabled.
                    if (!s_restoreComplete) Restore();
                    return;
                }
                s_restoreComplete = false;
                float now = Time.time;
                if (now < s_next) return;
                s_next = now + Interval;
                if (s_fastAmbientUntil > 0f && now >= s_fastAmbientUntil) RestoreAmbientFade();
                AudioListener listener = __instance.GetActiveAudioListener();
                if (listener == null) return;
                long rays = Cost.Start();
                if (s_pending)
                {
                    s_job.Complete();
                    s_pending = false;
                    Cost.Add(ref Cost.Rays, rays);
                    long c = Cost.Start();
                    Measure();
                    Cost.Add(ref Cost.Measure, c);
                }
                if (!Plugin.RoomReverb.Value) { RestoreZones(); EnsureArrays(); }
                else Setup(listener.transform);
                Vector3 from = Hearing.At;
                // The listener standing still sees almost identical space.
                // Do one full safety measurement per second, but immediately
                // refresh after movement or placement/door events.
                if (s_haveCast && !s_roomDirty &&
                    (from - s_lastCastPosition).sqrMagnitude < .18f * .18f &&
                    now - s_lastCastTime < StationaryResample)
                {
                    RoomStats.Skipped++;
                    return;
                }
                s_lastCastPosition = from; s_lastCastTime = now;
                s_haveCast = true; s_roomDirty = false;
                RoomStats.Scheduled++;
                s_from = from;
                var q = new QueryParameters(Mask, false, QueryTriggerInteraction.Ignore, false);
                for (int i = 0; i < Rays; i++)
                {
                    s_cmds[i] = new RaycastCommand(from, Dirs[i], q, MaxDistance);
                    // two beside it, 8 cm off (Picked)
                    Occlusion.Lines(from, from + Dirs[i], out Vector3 side, out Vector3 up);
                    s_cmds[Rays + 2 * i] = new RaycastCommand(from + side, Dirs[i], q, MaxDistance);
                    s_cmds[Rays + 2 * i + 1] = new RaycastCommand(from + up, Dirs[i], q, MaxDistance);
                }
                s_job = RaycastCommand.ScheduleBatch(s_cmds, s_hits, 16, 1);
                JobHandle.ScheduleBatchedJobs();
                s_pending = true;
            }
        }

        private static void EnsureArrays()
        {
            if (s_cmds.IsCreated) return;
            s_cmds = new NativeArray<RaycastCommand>(Rays * 3, Allocator.Persistent);
            s_hits = new NativeArray<RaycastHit>(Rays * 3, Allocator.Persistent);
        }

        private static void Setup(Transform listener)
        {
            EnsureArrays();
            if (s_zone == null)
            {
                var go = new GameObject("WiresAcousticsZone");
                s_zone = go.AddComponent<AudioReverbZone>();
                s_zone.reverbPreset = AudioReverbPreset.User;
                s_zone.minDistance = 5000f; // always inside it: full effect
                s_zone.maxDistance = 6000f;
                s_zone.diffusion = 100f;
                s_zone.density = 100f;
                s_zoneParent = null;
                s_haveCurrent = false;
                s_haveApplied = false;
            }
            if (s_zoneParent != listener)
            {
                s_zoneParent = listener;
                s_zone.transform.SetParent(listener, false);
                s_zone.transform.localPosition = Vector3.zero;
            }
            if (!s_zone.enabled) s_zone.enabled = true;
        }

        // Preserve the original components and enabled flags. Destroying the
        // game's zones made turning the mod off impossible to restore properly.
        internal static bool RemoveZone(AudioReverbZone z)
        {
            if (z == null || z == s_zone || !Plugin.Enabled.Value ||
                !Plugin.RoomReverb.Value || !z.enabled) return false;
            for (int i = 0; i < VanillaZones.Count; i++)
                if (VanillaZones[i].Zone == z) { z.enabled = false; return false; }
            if (VanillaZones.Count > 128)
                for (int i = VanillaZones.Count - 1; i >= 0; i--)
                    if (VanillaZones[i].Zone == null) VanillaZones.RemoveAt(i);
            VanillaZones.Add(new VanillaZone { Zone = z, Enabled = z.enabled });
            if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[acoustics] suspended vanilla reverb zone " + z.name);
            z.enabled = false;
            return true;
        }

        private static void RemoveVanilla()
        {
            if (!Plugin.RoomReverb.Value) { RestoreZones(); return; }
            foreach (AudioReverbZone z in Object.FindObjectsByType<AudioReverbZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                RemoveZone(z);
        }

        private static void RestoreZones()
        {
            if (s_zone != null && s_zone.enabled) s_zone.enabled = false;
            for (int i = 0; i < VanillaZones.Count; i++)
            {
                VanillaZone v = VanillaZones[i];
                if (v.Zone != null) v.Zone.enabled = v.Enabled;
            }
            VanillaZones.Clear();
            s_haveApplied = false;
        }

        private static void Restore()
        {
            s_restoreComplete = true;
            RestoreAmbientFade();
            RestoreZones();
            if (s_pending) { s_job.Complete(); s_pending = false; }
            Ambience.Reset();
            s_roomDirty = true;
            s_haveCast = false;
            Enclosure = 0f;
            WallBed = 0f;
            Portals.List.Clear();
        }

        internal static void Shutdown()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            Restore();
            if (s_zone != null) Object.Destroy(s_zone.gameObject);
            if (s_cmds.IsCreated) s_cmds.Dispose();
            if (s_hits.IsCreated) s_hits.Dispose();
        }

        // What each room ray landed on (the sea's surface included).
        internal static readonly bool[] Hit = new bool[Rays], Tree = new bool[Rays];
        private static readonly bool[] Built = new bool[Rays];
        private static readonly float[] Opening = new float[Rays]; // where a ray left through a gap, or -1
        private static readonly int[] SealedBy = new int[Rays]; // a lone gap's wall (the ray beside it), or -1
        // How open you are to the wind blowing from windFrom (0 sheltered, 1 out in it), and the way
        // it reaches you (Ambience): wind is air moving, and what stands upwind of you (a wall, a
        // rock, a hill) puts you in its lee; it comes round and over what's in the way. From the
        // room rays: the ones looking upwind and up, open past 12 m or out through a gap.
        internal static float Exposure(Vector3 windFrom, out Vector3 way)
        {
            float open = 0f, all = 0f;
            way = Vector3.zero;
            for (int i = 0; i < Rays; i++)
            {
                Vector3 d = Dirs[i];
                if (d.y < -.1f) continue;
                float w = Mathf.Max(0f, Vector3.Dot(d, windFrom)) + .3f * Mathf.Max(0f, d.y);
                if (w <= 0f) continue;
                all += w;
                if (Out(i) || Dist[i] > 12f) { open += w; way += d * w; }
            }
            if (way.sqrMagnitude > 1e-6f) way.Normalize();
            return all > 0f ? open / all : 1f;
        }

        // A ray that went out through an opening (or to open sky), not a seam (Portals).
        internal static bool Out(int i) => (!Hit[i] || Opening[i] >= 0f) && SealedBy[i] < 0;
        internal static readonly float[] Dist = new float[Rays], Alpha = new float[Rays];

        private static bool s_logDue;
        private static float s_logEnclosure, s_logNearest, s_logOpen, s_logVolume;
        private static int s_logGaps;
        private static string s_logNow = "";

        private static void WriteLog()
        {
            if (!s_logDue) return;
            s_logDue = false;
            Plugin.Log.LogInfo(s_logNow);
            Plugin.Log.LogInfo("[acoustics] space: enclosed " + s_logEnclosure.ToString("P0") + ", volume " + s_logVolume.ToString("F0") + " m3 (" + s_logGaps + " rays out through gaps), size " + s_path.ToString("F1") + " m (nearest " +
                s_logNearest.ToString("F1") + " m), absorption " + s_alpha.ToString("F2") + " (open " + s_logOpen.ToString("P0") + "), RT60 " +
                s_decay.ToString("F2") + " s -> " + s_current + "; " + Portals.Summary() + "; " + Ocean.Summary() + "; " + Ambience.Summary() + "; " + Spawned.Summary() + ", following their owner " + Follow.Count + "; " + Underwater.Summary() + "; " + Occlusion.Summary() + "; " + Budget.Summary() + "; " + Budget.Loudness() + "; " + AcousticFilter.DiagnosticsSummary() + "; " +
                Batch.Summary() + "; " + RoomStats.Summary() + "; " + Cost.Report() + "; " + Budget.Toggles());
        }

        // Each room ray is three, 8 cm apart. One that got through where both beside it hit something
        // slipped through a seam between pieces: it counts as hitting what they hit (the nearer).
        // Openings you see are wider than that, and all three get through them.
        private static readonly RaycastHit[] Picked = new RaycastHit[Rays];
        private static RaycastHit Pick(int i)
        {
            RaycastHit h = s_hits[i];
            if (h.colliderInstanceID != 0) return h;
            RaycastHit a = s_hits[Rays + 2 * i], b = s_hits[Rays + 2 * i + 1];
            if (a.colliderInstanceID == 0 || b.colliderInstanceID == 0) return h;
            return a.distance <= b.distance ? a : b;
        }

        private static void Measure()
        {
            int level = 0, hits = 0;
            // The outside through the walls round you (Ambience's muffled bed): what the walls you're
            // surrounded by let through as the first wall a sound comes through (Occlusion), averaged.
            float bedSum = 0f; int bedWalls = 0;
            float levelHits = 0f;
            float pathSum = 0f, alphaSum = 0f, escaped = 0f;
            float near0 = MaxDistance, near1 = MaxDistance, near2 = MaxDistance;
            for (int i = 0; i < Rays; i++)
            {
                Picked[i] = Pick(i);
                Hit[i] = Surface(s_from, Dirs[i], Picked[i], MaxDistance, out _, out _, out Dist[i], out Alpha[i], out Tree[i]);
                Built[i] = Hit[i] && (Solids.Of(Picked[i])?.Built ?? false);
            }
            // Gaps: a ray that leaves a building through a window, a door or a crack goes on to the sky
            // or to a hill far off, and taking that as part of the room made rooms seem many times their
            // size. A ray that ends past every built surface next to it, on something that isn't built
            // (or nothing), went out through an opening; the opening is where those built surfaces are.
            int[][] nb = Portals.NeighboursOf(Dirs);
            for (int i = 0; i < Rays; i++)
            {
                Opening[i] = -1f;
                if (Built[i]) continue;
                float wall = -1f;
                foreach (int k in nb[i]) if (Built[k]) wall = Mathf.Max(wall, Dist[k]);
                if (wall > 0f && (!Hit[i] || Dist[i] > wall)) Opening[i] = wall;
            }
            // A window or a door is several rays wide; a lone gap with built surfaces all round it is a
            // seam between pieces (roofs and floors are full of them) that the eye sees as solid. For
            // what comes in from outside it counts as the wall it's in.
            for (int i = 0; i < Rays; i++)
            {
                SealedBy[i] = -1;
                if (Opening[i] < 0f) continue;
                int by = -1;
                foreach (int k in nb[i])
                {
                    if (!Built[k]) { by = -1; break; }
                    if (by < 0 || Dist[k] > Dist[by]) by = k;
                }
                SealedBy[i] = by;
            }
            for (int i = 0; i < Rays; i++)
            {
                bool hit = Hit[i] && Opening[i] < 0f;
                // a tree trunk isn't a wall: a forest only half encloses you (TreeWeight), a swamp
                // full of trunks isn't a cave
                if (Dirs[i].y > -.05f)
                {
                    level++;
                    if (hit) levelHits += Tree[i] ? TreeWeight : 1f;
                }
                int wallRay = hit && Built[i] ? i : SealedBy[i];
                if (wallRay >= 0)
                {
                    Solids.Info s = Solids.Of(Picked[wallRay]);
                    if (s != null && s.Wall) { bedSum += s.First * s.First; bedWalls++; }
                }
                if (!hit) { escaped++; continue; }
                hits++;
                float d = Dist[i];
                pathSum += d;
                alphaSum += Alpha[i];
                if (d < near0) { near2 = near1; near1 = near0; near0 = d; }
                else if (d < near1) { near2 = near1; near1 = d; }
                else if (d < near2) near2 = d;
            }
            float enclosure = level > 0 ? levelHits / level : 0f;
            float path = hits > 0 ? pathSum / hits : MaxDistance;
            float alpha = hits > 0 ? alphaSum / hits : 1f;
            float alphaEff = Mathf.Max(.01f, (alphaSum + escaped) / Rays); // open directions let the sound out
            // Critical distance (how much reverb each sound gets) from the room as measured.
            float decayAsBuilt = Mathf.Clamp(.04f * path / alphaEff, .1f, 8f);
            // What the reverb sounds like comes from three things: how big the space is, what it's made
            // of, and how closed in you are.
            // Size: Valheim rooms are built at about twice real size and bare, so taken literally they
            // ring like halls. Heard as the real rooms they stand for: half the size (RoomScale), and
            // with the soft furnishings real homes have (LivedIn) indoors.
            float decay = Mathf.Clamp(.04f * path * RoomScale / (alphaEff + LivedIn * enclosure), .1f, 8f);
            float volume = 0f;
            for (int i = 0; i < Rays; i++) { float r = Opening[i] >= 0f ? Opening[i] : Hit[i] ? Dist[i] : MaxDistance; volume += r * r * r; }
            volume *= 4f * Mathf.PI / (3f * Rays);
            float critical = .057f * Mathf.Sqrt(volume / decayAsBuilt);
            float nearest = (near0 + near1 + near2) / 3f;

            // The reverberant level, as a real room's: proportional to (1 - a) / a (the room constant), so
            // a bare stone room rings, a furnished one barely does. 0 dB at a = 0.05.
            // No source for these levels: a judgement mapping (Unity's millibels measured in game,
            // but a physical model for the level, Barron's, sounded far too wet and was dropped).
            float reverbLevel = Mathf.Clamp(1000f * Mathf.Log10((1f - alpha) / alpha * .05f / .95f), -3000f, 0f); // millibels
            float wet = Mathf.Clamp01(enclosure * (1f + reverbLevel / 3000f));
            // Indoors, cozy: a lived-in room's first reflections come back soft (rugs, hangings, people)
            // and its ring is dark, not a bare box's slap. 3 dB less of the first reflections and
            // 4 dB less of the highs the more closed in you are (by ear, no source).
            float cozy = enclosure;
            var room = new Params
            {
                Room = Mathf.Lerp(-2600f, -150f + reverbLevel, enclosure),
                // the tone as 0.1.2 had it (by ear): the more the room absorbs, the warmer its reverb
                RoomHF = Mathf.Lerp(-150f, -2600f, Mathf.Clamp01(alpha * 2.5f)) - 400f * cozy,
                Decay = decay,
                DecayHF = Mathf.Lerp(1.2f, .3f, Mathf.Clamp01(alpha * 2.5f)),
                Reflections = Mathf.Lerp(-2600f, -250f + reverbLevel * .6f, enclosure) - 300f * cozy,
                ReflectionsDelay = Mathf.Clamp(2f * nearest * RoomScale / 343f, .002f, .3f),
                Reverb = Mathf.Lerp(-2200f, 200f + reverbLevel, wet),
                ReverbDelay = Mathf.Clamp(path * RoomScale / 343f, .005f, .1f)
            };
            // Out in the open the same: open sky lets the sound out, so it's dry, apart from the
            // ground and what stands round you.
            Params target = room;
            float strength = Plugin.ReverbStrength.Value;
            float gain = strength > .001f ? 2000f * Mathf.Log10(strength) : -10000f; // millibels
            target.Room += gain;
            target.Reverb += gain;
            target.Reflections += gain;

            // Doorways/dungeon exits should not keep the previous room's wet settings
            // for several measurement ticks.  Keep the normal stable-room smoothing,
            // but respond quickly when the enclosure itself changes substantially.
            // The actual reverb tail is left to Unity's DSP; new outdoor sounds get
            // the new (dry) zone settings promptly.
            float change = enclosure - Enclosure;
            // Detect a real change from an enclosed room to open air (or back),
            // not an ordinary outdoor weather/biome change. The 3-second window
            // allows EnvMan's environment selection to catch up with geometry.
            if (s_haveCurrent && Mathf.Abs(change) > .12f &&
                (enclosure >= .35f || Enclosure >= .35f)) SpeedAmbientAtBoundary();
            float response = Mathf.Abs(change) > .12f ? (change < 0f ? .88f : .70f) : Smooth;
            // The reverb through a doorway: a real tail doesn't stop when you step out, it dies away
            // over its own length (RT60). So stepping out of a space glides over that space's reverb
            // time (a small room's in a tick, a cave's over seconds). Stepping in stays quick: what
            // you hear in there from then on is that space's.
            float tail = !s_haveCurrent || Mathf.Abs(change) <= .12f ? Smooth
                : change > 0f ? response
                : 1f - Mathf.Exp(-3f * Interval / Mathf.Max(.1f, s_current.Decay)); // 95% of the way in one RT60
            s_current = s_haveCurrent ? Params.Lerp(s_current, target, tail) : target;
            s_haveCurrent = true;
            Enclosure = Mathf.Lerp(Enclosure, enclosure, response);
            WallBed = Mathf.Lerp(WallBed, bedWalls > 0 ? Mathf.Sqrt(bedSum / bedWalls) : 0f, response);
            Critical = Mathf.Lerp(Critical, critical, response);
            s_path = path; s_alpha = alpha; s_decay = decay;
            // Only when it changed audibly (setting the zone's values is main-thread work).
            if (s_zone != null && Plugin.RoomReverb.Value && (!s_haveApplied || s_current.Differs(s_applied)))
            {
                s_current.ApplyTo(s_zone);
                s_applied = s_current;
                s_haveApplied = true;
            }
            Portals.Find(s_from, Dirs, enclosure, path);
            if (AudioMan.instance != null) Ambience.Update(AudioMan.instance, Enclosure);

            if (Plugin.DebugLog.Value && Time.time >= s_nextLog)
            {
                // written on the next tick, so building the summary doesn't add to this one
                s_nextLog = Time.time + 3f;
                s_logDue = true;
                s_logEnclosure = enclosure; s_logNearest = nearest; s_logOpen = escaped / Rays;
                s_logVolume = volume; s_logGaps = 0;
                for (int i = 0; i < Rays; i++) if (Opening[i] >= 0f) s_logGaps++;
                s_logNow = "[acoustics] now: " + (enclosure >= .35f ? "inside" : "outside") + " (closed in " + enclosure.ToString("P0") + "), space " +
                    (path * RoomScale).ToString("F1") + " m heard (" + path.ToString("F1") + " m built), reverb " + decay.ToString("F2") + " s, " + (alpha < .05f ? "bright" : alpha < .12f ? "warm" : "soft") +
                    "; " + Ambience.Now();
            }
        }

        // Trees, as the game itself defines them (Solids). They scatter sound more than they reflect
        // it, so they count little toward enclosure, absorb most of what reaches them (for the reverb),
        // and muffle little.
        internal const float TreeWeight = .15f; // no source: a judgement value

        internal const float WaterAbsorb = .01f; // a calm water surface reflects nearly everything

        // What a ray ran into: its first hit, or the sea's surface if it reached that first (rays pass
        // through water, so going down they'd hit the sea bed under it). False: nothing in range.
        internal static bool Surface(Vector3 from, Vector3 dir, RaycastHit h, float max, out Vector3 point, out Vector3 normal, out float dist, out float alpha, out bool tree)
        {
            float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            if (dir.y < -.01f && from.y > sea && (h.colliderInstanceID == 0 || h.point.y < sea))
            {
                float t = (from.y - sea) / -dir.y;
                if (t <= max)
                {
                    point = from + dir * t; normal = Vector3.up; dist = t; alpha = WaterAbsorb; tree = false;
                    return true;
                }
            }
            Solids.Info s = Solids.Of(h);
            if (s == null)
            {
                point = default; normal = Vector3.up; dist = max; alpha = 1f; tree = false;
                return false;
            }
            point = h.point; normal = h.normal; dist = h.distance;
            tree = s.Tree;
            alpha = s.Absorb >= 0f ? s.Absorb : s.Hm != null ? GroundAt(s.Hm, h.point) : GroundClass[3];
            return true;
        }

        // Furniture and clutter at 500 Hz (pyroomacoustics' material database): beds as empty
        // cloth-covered upholstered chairs, rugs, hides and furs as hairy carpet on felt, banners,
        // tapestries and curtains as tight velvet curtains, anything else as wooden chairs.
        private static float Furniture(Piece p, WearNTear wnt)
        {
            string n = wnt.gameObject.name.ToLowerInvariant();
            if (p != null && p.m_comfortGroup == Piece.ComfortGroup.Bed) return .77f;
            if ((p != null && p.m_comfortGroup == Piece.ComfortGroup.Carpet) || n.Contains("rug") || n.Contains("hide") || n.Contains("fur") || n.Contains("pillow"))
                return .37f;
            if ((p != null && p.m_comfortGroup == Piece.ComfortGroup.Banner) || n.Contains("tapestry") || n.Contains("curtain") || n.Contains("cloth"))
                return .35f;
            return .10f;
        }

        // The natural ground, by the Nord2000 ground classes (effective flow resistivity in kPa s/m2,
        // as listed in d&b's NoizCalc white paper and Bies, Hansen and Howard, Engineering Noise
        // Control, table 5.2): A 12.5 snow or moss, B 31.5 soft forest floor, thick moss, C 80 turf,
        // grass, loose soil, D 200 normal uncompacted ground, field, forest floor, E 500 compacted
        // field and gravel, F 2000 gravel road, G 20000 asphalt, concrete, H 200000 dense asphalt,
        // water. Each turned into random-incidence absorption at 500 Hz the way Nord2000 itself does
        // it: Delany and Bazley's impedance for a thick porous ground, locally reacting, averaged over
        // every angle of arrival.
        private static readonly float[] GroundClass = { .891f, .791f, .611f, .413f, .251f, .105f, .022f, .004f }; // A to H

        // Which class the ground is at a spot: first what the player made of it (paved is -1: stone
        // paving, absorbing like concrete and tile), else what the biome's ground is.
        private static int GroundClassAt(Heightmap hm, Vector3 p)
        {
            Texture2D mask = hm.GetPaintMask();
            if (mask != null)
            {
                try
                {
                    Color c = hm.GetPaintMask(p);
                    if (c.b > .5f) return -1; // paved
                    if (c.g > .5f) return 2;  // tilled: loose soil
                    if (c.r > .5f) return 4;  // dug or cleared: compacted
                }
                catch (System.Exception) { }
            }
            switch (hm.GetBiome(p))
            {
                case Heightmap.Biome.Meadows: return 2;     // grass
                case Heightmap.Biome.Plains: return 2;      // grass, dry
                case Heightmap.Biome.BlackForest: return 1; // moss and needles
                case Heightmap.Biome.Mountain: return 0;    // snow
                case Heightmap.Biome.DeepNorth: return 0;   // snow
                case Heightmap.Biome.Mistlands: return 3;   // forest floor
                case Heightmap.Biome.Swamp: return 3;       // mud
                case Heightmap.Biome.AshLands: return 5;    // packed ash and rock
                case Heightmap.Biome.Ocean: return 4;       // sand
                default: return 3;
            }
        }

        private static float GroundAt(Heightmap hm, Vector3 p)
        {
            int k = GroundClassAt(hm, p);
            return k < 0 ? .03f : GroundClass[k];
        }

        // Absorption coefficient (share of sound energy a surface swallows, mid frequencies) by what
        // something is, worked out once per collider (Solids). -1: the ground, by the exact spot.
        internal static float Absorption(Collider c, Piece p, WearNTear wnt, bool structural, bool ground, bool tree)
        {
            if (wnt != null && !structural)
            {
                // Furniture and clutter: chests, benches, stations, racks, beds, rugs. They break up
                // and swallow the reflections that make an empty room ring; soft ones most (the game's
                // own comfort groups for beds, banners and carpets, plus anything cloth or fur by name).
                return Furniture(p, wnt);
            }
            if (wnt != null)
            {
                switch (wnt.m_materialType)
                {
                    case WearNTear.MaterialType.Stone: case WearNTear.MaterialType.Ashstone: return .03f;
                    case WearNTear.MaterialType.Marble: return .01f; // marble/tile at 500 Hz (Sengpiel)
                    case WearNTear.MaterialType.Iron: return .08f; // steel decking at 500 Hz
                    case WearNTear.MaterialType.Ancient: return .06f; // heavy wood at 500 Hz, as below
                    case WearNTear.MaterialType.Ice: return .013f; // water or ice surface at 500 Hz (Sengpiel)
                    case WearNTear.MaterialType.HardWood: case WearNTear.MaterialType.Timberwood: return .06f; // heavy wood at 500 Hz (Vorlander, Auralization: wood stage floor, 2 layers 27 mm)
                    default: return .10f; // wood planks: wood on joists at 500 Hz (Sengpiel)
                }
            }
            if (ground) return -1f;                                        // the ground: by the exact point (Surface)
            if (tree) return .05f;                                         // bark at 500 Hz (Li et al. 2020)
            if (c.gameObject.layer == Solids.StaticSolid) return .03f;     // rock: limestone wall at 500 Hz (pyroomacoustics database)
            return .15f;                                                   // props: no source, a judgement value
        }
    }

    // Everything the mod needs to know about a collider, worked out the first time a ray hits it and
    // kept by its instance id: one table instead of the eight there were (built, structural, tree,
    // ground, weight, wall loss, mass, absorption), each doing its own GetComponentInParent calls. A
    // hit is looked up by RaycastHit.colliderInstanceID, a plain number; RaycastHit.collider is a
    // native object lookup every time it's read (Object.FindObjectFromInstanceID, Unity's source),
    // so it's only read for a collider not seen before.
    // The old tables were emptied at 8192 entries; a big base has more colliders than that, and each
    // emptying meant working every one out again. This holds 65536 (an entry is a few dozen bytes).
    // Ids aren't reused in a session, so a destroyed collider's entry just sits until then.
    // A built piece, rock or the ground doesn't move, so its box is kept too (read again after any
    // building change: a door swinging, WorldGen); anything else (a ship, a cart, a creature) is
    // read live.
    internal static class Solids
    {
        internal sealed class Info
        {
            internal Collider C;
            internal Transform Root;
            internal bool Built, Structural, Tree, Ground, Wall, Static;
            internal float Weight, First = 1f, Cut = 22000f, Absorb;
            internal Heightmap Hm;
            internal Bounds Box;
            internal int BoxGen = -1;
        }

        private const int Cap = 1 << 16;
        private static readonly Dictionary<int, Info> Table = new Dictionary<int, Info>(4096);
        internal static readonly int StaticSolid = LayerMask.NameToLayer("static_solid");
        private static readonly int PieceLayer = LayerMask.NameToLayer("piece"), TerrainLayer = LayerMask.NameToLayer("terrain");

        internal static Info Of(in RaycastHit h)
        {
            int id = h.colliderInstanceID;
            if (id == 0) return null;
            return Table.TryGetValue(id, out Info i) ? i : Make(id, h.collider);
        }

        internal static Info Of(Collider c)
        {
            if (c == null) return null;
            int id = c.GetInstanceID();
            return Table.TryGetValue(id, out Info i) ? i : Make(id, c);
        }

        internal static Bounds BoxOf(Info s)
        {
            if (!s.Static) return s.C != null ? s.C.bounds : s.Box;
            if (s.BoxGen != Occlusion.WorldGen && s.C != null) { s.Box = s.C.bounds; s.BoxGen = Occlusion.WorldGen; }
            return s.Box;
        }

        private static Info Make(int id, Collider c)
        {
            if (c == null) return null;
            if (Table.Count >= Cap) Table.Clear();
            var s = new Info { C = c, Root = c.transform.root };
            int layer = c.gameObject.layer;
            s.Static = layer == PieceLayer || layer == StaticSolid || layer == TerrainLayer;
            s.Box = c.bounds;
            s.BoxGen = Occlusion.WorldGen;
            WearNTear wnt = c.GetComponentInParent<WearNTear>();
            s.Built = wnt != null;
            s.Hm = c.GetComponentInParent<Heightmap>();
            s.Ground = s.Hm != null;
            Vector3 size = s.Box.size;
            bool big = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) >= 1.8f;
            Piece p = c.GetComponentInParent<Piece>();
            if (p == null) s.Structural = big;
            else if (p.m_category == Piece.PieceCategory.BuildingWorkbench || p.m_category == Piece.PieceCategory.BuildingStonecutter) s.Structural = true;
            else if (p.m_category == Piece.PieceCategory.DeepNorth) s.Structural = big;
            if (!s.Built && !s.Ground)
            {
                // what the game itself calls a tree: cuttable trees, felled logs, and destructibles of the tree kind
                Destructible d = c.GetComponentInParent<Destructible>();
                s.Tree = c.GetComponentInParent<TreeBase>() != null || c.GetComponentInParent<TreeLog>() != null ||
                    (d != null && d.m_destructibleType == DestructibleType.Tree);
            }
            Occlusion.Classify(s, wnt, layer);
            s.Absorb = Room.Absorption(c, p, wnt, s.Structural, s.Ground, s.Tree);
            Table[id] = s;
            return s;
        }
    }
}
