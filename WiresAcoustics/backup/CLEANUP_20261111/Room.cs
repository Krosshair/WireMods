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
        private static readonly Dictionary<int, float> Absorb = new Dictionary<int, float>();
        // Save original Unity reverb zones rather than destroying them.
        // Required for toggling acoustics OFF without reloading the area.
        private struct VanillaZone { internal AudioReverbZone Zone; internal bool Enabled; }
        private static readonly List<VanillaZone> VanillaZones = new List<VanillaZone>(48);
        private static bool s_roomDirty = true, s_haveCast, s_restoreComplete;
        private static Vector3 s_lastCastPosition;
        private static float s_lastCastTime;
        private const float StationaryResample = 1f;
        internal static void Invalidate() { s_roomDirty = true; RoomStats.Invalidated++; }
        internal static void ReapplyZone() { s_haveApplied = false; Invalidate(); }


        // Measured, smoothed (read by Occlusion for the per-sound reverb send).
        internal static float Enclosure, Absorption = .3f;
        // The outside's energy reaching you through the walls, floors and roofs round you (share of
        // directions times what each lets through, squared): the rest comes in through the openings.
        internal static float WallEnergy;
        // Where a sound is as much reverb as direct (the critical distance, Hopkins-Stryker as Arup
        // gives it): D = 0.057 sqrt(Q V / RT), Q = 1 for a sound sending all round, V the space's
        // volume (from the room rays, each ray's cone out to what it hit), RT the reverb time.
        internal static float Critical = 5f;
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
                if (__instance.GetActiveAudioListener() is AudioListener cl) Calibrate.Step(s_zone, cl.transform);
                if (s_wasOn && !on) { PortalLoop.Clear(); Travel.Clear(); Batch.Flush(); AcousticFilter.ReleaseAll(); PlayerSounds.RestoreAll(); Ambience.RestorePlaced(); Underwater.Reset(); Follow.Clear(); }
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
                    Underwater.Update(lt.position, Time.deltaTime);
                    AcousticFilter.Strength = Mathf.Clamp(Plugin.HrtfStrength.Value, 0f, 2f);
                    long c = Cost.Start();
                    if (r != 0L) rest += c - r;
                    Follow.Step();
                    PortalLoop.Step(lt);
                    Cost.Add(ref Cost.Follow, c);
                    c = Cost.Start();
                    Directions.Step(lt);
                    Cost.Add(ref Cost.Dirs, c);
                    r = Cost.Start();
                    Ambience.Pan(__instance, lt, Time.deltaTime);
                    float t = Time.time;
                    c = Cost.Start();
                    if (r != 0L) rest += c - r;
                    if (t >= s_nextVoice) { s_nextVoice = t + Interval; Occlusion.Voices(lt.position); }
                    if (t >= s_nextRecheck) { s_nextRecheck = t + .5f; Occlusion.Recheck(lt.position); }
                    Cost.Add(ref Cost.Recheck, c);
                    if (r != 0L) Cost.AddTicks(ref Cost.Rest, rest);
                    c = Cost.Start();
                    Budget.Step(t);
                    Cost.Add(ref Cost.Budget, c);
                    c = Cost.Start();
                    Travel.Step(t);
                    Cost.Add(ref Cost.Travel, c);
                    c = Cost.Start();
                    Batch.Step(lt.position);
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
                Bounces.Complete();
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
                Vector3 from = listener.transform.position;
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
                for (int i = 0; i < Rays; i++) s_cmds[i] = new RaycastCommand(from, Dirs[i], q, MaxDistance);
                s_job = RaycastCommand.ScheduleBatch(s_cmds, s_hits, 16, 1);
                JobHandle.ScheduleBatchedJobs();
                s_pending = true;
            }
        }

        private static void EnsureArrays()
        {
            if (s_cmds.IsCreated) return;
            s_cmds = new NativeArray<RaycastCommand>(Rays, Allocator.Persistent);
            s_hits = new NativeArray<RaycastHit>(Rays, Allocator.Persistent);
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
            WallEnergy = 0f;
            Portals.List.Clear();
            Bounces.Clear();
        }

        internal static void Shutdown()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            Restore();
            if (s_zone != null) Object.Destroy(s_zone.gameObject);
            if (s_cmds.IsCreated) s_cmds.Dispose();
            if (s_hits.IsCreated) s_hits.Dispose();
        }

        // What each room ray landed on, for the bounces too (the sea's surface included).
        internal static readonly bool[] Hit = new bool[Rays], Tree = new bool[Rays];
        private static readonly bool[] Built = new bool[Rays];
        private static readonly float[] Opening = new float[Rays]; // where a ray left through a gap, or -1
        private static readonly Dictionary<int, bool> BuiltCache = new Dictionary<int, bool>();
        private static bool IsBuilt(Collider c)
        {
            if (c == null) return false;
            int id = c.GetInstanceID();
            if (BuiltCache.TryGetValue(id, out bool b)) return b;
            if (BuiltCache.Count > 8192) BuiltCache.Clear();
            b = c.GetComponentInParent<WearNTear>() != null;
            BuiltCache[id] = b;
            return b;
        }
        internal static readonly Vector3[] Point = new Vector3[Rays], Normal = new Vector3[Rays];
        internal static readonly float[] Dist = new float[Rays], Alpha = new float[Rays];

        private static bool s_logDue;
        private static float s_logEnclosure, s_logNearest, s_logOpen, s_logVolume;
        private static int s_logGaps;

        private static void WriteLog()
        {
            if (!s_logDue) return;
            s_logDue = false;
            Plugin.Log.LogInfo("[acoustics] space: enclosed " + s_logEnclosure.ToString("P0") + ", volume " + s_logVolume.ToString("F0") + " m3 (" + s_logGaps + " rays out through gaps), size " + s_path.ToString("F1") + " m (nearest " +
                s_logNearest.ToString("F1") + " m), absorption " + s_alpha.ToString("F2") + " (open " + s_logOpen.ToString("P0") + "), RT60 " +
                s_decay.ToString("F2") + " s -> " + s_current + "; " + Portals.Summary() + "; " + Bounces.Summary() + "; " + Ocean.Summary() + "; " + Ambience.Summary() + "; " + Ambience.WildlifeDiagnostics() + "; " + Spawned.Summary() + ", following their owner " + Follow.Count + "; " + Underwater.Summary() + "; " + Occlusion.Summary() + ", " + Travel.Summary() + "; " + Budget.Summary() + "; " + Budget.Loudness() + "; " + AcousticFilter.DiagnosticsSummary() + "; " +
                Batch.Summary() + "; " + RoomStats.Summary() + "; " + Cost.Report() + "; " + Budget.Toggles());
        }

        private static void Measure()
        {
            int level = 0, hits = 0;
            float wallEnergy = 0f;
            float levelHits = 0f;
            float pathSum = 0f, alphaSum = 0f, escaped = 0f, lowSum = 0f, highSum = 0f;
            float near0 = MaxDistance, near1 = MaxDistance, near2 = MaxDistance;
            for (int i = 0; i < Rays; i++)
            {
                Hit[i] = Surface(s_from, Dirs[i], s_hits[i], MaxDistance, out Point[i], out Normal[i], out Dist[i], out Alpha[i], out Tree[i]);
                Built[i] = Hit[i] && IsBuilt(s_hits[i].collider);
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
            for (int i = 0; i < Rays; i++)
            {
                bool hit = Hit[i] && Opening[i] < 0f;
                // a tree trunk isn't a wall: a forest only half encloses you (TreeWeight), a swamp
                // full of trunks isn't a cave
                if (Dirs[i].y > -.05f)
                {
                    level++;
                    if (hit && Tree[i]) { levelHits += TreeWeight; wallEnergy += TreeWeight; }
                    else if (hit)
                    {
                        levelHits += 1f;
                        float pass = Occlusion.Pass(s_hits[i].collider);
                        wallEnergy += pass * pass;
                    }
                }
                if (!hit) { escaped++; continue; }
                hits++;
                float d = Dist[i];
                pathSum += d;
                alphaSum += Alpha[i];
                Vector2 band = BandsAt(i);
                lowSum += band.x; highSum += band.y;
                if (d < near0) { near2 = near1; near1 = near0; near0 = d; }
                else if (d < near1) { near2 = near1; near1 = d; }
                else if (d < near2) near2 = d;
            }
            float enclosure = level > 0 ? levelHits / level : 0f;
            float path = hits > 0 ? pathSum / hits : MaxDistance;
            float alpha = hits > 0 ? alphaSum / hits : 1f;
            float alphaEff = Mathf.Max(.01f, (alphaSum + escaped) / Rays); // open directions let the sound out
            float decay = Mathf.Clamp(.04f * path / alphaEff, .1f, 8f);
            // Highs (the zone's reference, 5 kHz): Sabine with the 4 kHz absorption and the air's at
            // 5 kHz, as a ratio of the low decay (250 Hz absorption); and how much less of the highs
            // each reflection gives back, plus the air over one mean free path.
            float airHigh = AcousticFilter.AirDbPerMetre(5000f), airLow = AcousticFilter.AirDbPerMetre(250f);
            float rtLow = .04f * path / Mathf.Max(.01f, (lowSum + escaped) / Rays + airLow / 4.343f * path);
            float rtHigh = .04f * path / Mathf.Max(.01f, (highSum + escaped) / Rays + airHigh / 4.343f * path);
            float aLow = hits > 0 ? lowSum / hits : 1f, aHigh = hits > 0 ? highSum / hits : 1f;
            float hfEnergy = (1f - Mathf.Clamp(aHigh, 0f, .99f)) / (1f - Mathf.Clamp(aLow, 0f, .99f)) * Mathf.Pow(10f, -(airHigh - airLow) * path / 10f);
            float volume = 0f;
            for (int i = 0; i < Rays; i++) { float r = Opening[i] >= 0f ? Opening[i] : Hit[i] ? Dist[i] : MaxDistance; volume += r * r * r; }
            volume *= 4f * Mathf.PI / (3f * Rays);
            float critical = .057f * Mathf.Sqrt(volume / decay);
            float nearest = (near0 + near1 + near2) / 3f;

            // The reverberant level, as a real room's: proportional to (1 - a) / a (the room constant), so
            // a bare stone room rings, a furnished one barely does. 0 dB at a = 0.05.
            float reverbLevel = Mathf.Clamp(1000f * Mathf.Log10((1f - alpha) / alpha * .05f / .95f), -3000f, 0f); // millibels
            float wet = Mathf.Clamp01(enclosure * (1f + reverbLevel / 3000f));
            var room = new Params
            {
                Room = Mathf.Lerp(-2600f, -150f + reverbLevel, enclosure),
                RoomHF = Mathf.Clamp(1000f * Mathf.Log10(Mathf.Max(1e-4f, hfEnergy)), -10000f, 0f),
                Decay = decay,
                DecayHF = Mathf.Clamp(rtHigh / Mathf.Max(.01f, rtLow), .1f, 2f),
                Reflections = Mathf.Lerp(-2600f, -250f + reverbLevel * .6f, enclosure),
                ReflectionsDelay = Mathf.Clamp(2f * nearest / 343f, .002f, .3f),
                Reverb = Mathf.Lerp(-2200f, 200f + reverbLevel, wet),
                ReverbDelay = Mathf.Clamp(path / 343f, .005f, .1f)
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
            s_current = s_haveCurrent ? Params.Lerp(s_current, target, response) : target;
            s_haveCurrent = true;
            Enclosure = Mathf.Lerp(Enclosure, enclosure, response);
            WallEnergy = Mathf.Lerp(WallEnergy, level > 0 ? wallEnergy / level : 0f, response);
            Critical = Mathf.Lerp(Critical, critical, response);
            Absorption = Mathf.Lerp(Absorption, alpha, response);
            s_path = path; s_alpha = alpha; s_decay = decay;
            // Only when it changed audibly (setting the zone's values is main-thread work).
            if (s_zone != null && Plugin.RoomReverb.Value && !Calibrate.Running && (!s_haveApplied || s_current.Differs(s_applied)))
            {
                s_current.ApplyTo(s_zone);
                s_applied = s_current;
                s_haveApplied = true;
            }
            Portals.Find(s_from, Dirs, s_hits, enclosure, path);
            Bounces.Schedule(Dirs);
            if (AudioMan.instance != null) Ambience.Update(AudioMan.instance, Enclosure);

            if (Plugin.DebugLog.Value && Time.time >= s_nextLog)
            {
                // written on the next tick, so building the summary doesn't add to this one
                s_nextLog = Time.time + 3f;
                s_logDue = true;
                s_logEnclosure = enclosure; s_logNearest = nearest; s_logOpen = escaped / Rays;
                s_logVolume = volume; s_logGaps = 0;
                for (int i = 0; i < Rays; i++) if (Opening[i] >= 0f) s_logGaps++;
            }
        }

        // A wall, floor, roof, beam or pole, as opposed to furniture, stations and props: by the piece's
        // build category (the hammer tabs), size (1.8 m) for anything else. Cached per collider.
        private static readonly Dictionary<int, bool> StructuralCache = new Dictionary<int, bool>();
        internal static bool Structural(Collider c)
        {
            int id = c.GetInstanceID();
            if (StructuralCache.TryGetValue(id, out bool s)) return s;
            if (StructuralCache.Count > 8192) StructuralCache.Clear();
            Piece p = c.GetComponentInParent<Piece>();
            bool big = Mathf.Max(c.bounds.size.x, Mathf.Max(c.bounds.size.y, c.bounds.size.z)) >= 1.8f;
            if (p == null) s = big;
            else if (p.m_category == Piece.PieceCategory.BuildingWorkbench || p.m_category == Piece.PieceCategory.BuildingStonecutter) s = true;
            else if (p.m_category == Piece.PieceCategory.DeepNorth) s = big;
            else s = false;
            StructuralCache[id] = s;
            return s;
        }

        // Trees, as the game itself defines them: cached per collider. They scatter sound more than they
        // reflect it, so they count little toward enclosure, absorb most of what reaches them (for
        // the reverb and bounce paths), and muffle little.
        internal const float TreeWeight = .15f;
        private static readonly Dictionary<int, bool> TreeCache = new Dictionary<int, bool>();
        internal static bool IsTree(Collider c)
        {
            int id = c.GetInstanceID();
            if (TreeCache.TryGetValue(id, out bool t)) return t;
            if (TreeCache.Count > 8192) TreeCache.Clear();
            t = false;
            if (c.GetComponentInParent<WearNTear>() == null && c.GetComponentInParent<Heightmap>() == null)
            {
                // what the game itself calls a tree: cuttable trees, felled logs, and destructibles of the tree kind
                Destructible d = c.GetComponentInParent<Destructible>();
                t = c.GetComponentInParent<TreeBase>() != null || c.GetComponentInParent<TreeLog>() != null ||
                    (d != null && d.m_destructibleType == DestructibleType.Tree);
            }
            TreeCache[id] = t;
            return t;
        }

        // Absorption coefficient (share of sound energy a surface swallows, mid frequencies) by what the
        // ray hit. Cached per collider.
        private static readonly Dictionary<int, Heightmap> Ground = new Dictionary<int, Heightmap>();
        internal const float WaterAbsorb = .01f; // a calm water surface reflects nearly everything

        // What a ray ran into: its first hit, or the sea's surface if it reached that first (rays pass
        // through water, so going down they'd hit the sea bed under it). False: nothing in range.
        internal static bool Surface(Vector3 from, Vector3 dir, RaycastHit h, float max, out Vector3 point, out Vector3 normal, out float dist, out float alpha, out bool tree)
        {
            float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            if (dir.y < -.01f && from.y > sea && (h.collider == null || h.point.y < sea))
            {
                float t = (from.y - sea) / -dir.y;
                if (t <= max)
                {
                    point = from + dir * t; normal = Vector3.up; dist = t; alpha = WaterAbsorb; tree = false;
                    return true;
                }
            }
            if (h.collider == null)
            {
                point = default; normal = Vector3.up; dist = max; alpha = 1f; tree = false;
                return false;
            }
            point = h.point; normal = h.normal; dist = h.distance;
            tree = IsTree(h.collider);
            alpha = AbsorptionAt(h.collider, h.point);
            return true;
        }

        // Absorption per band, 250 Hz and 4 kHz, from published coefficients (Sengpiel's table):
        // unglazed brick for stone, marble or tile, wood on joists for wood, hardwood, water or ice
        // surface, concrete or tile for paving, steel decking for iron, bark for trees (Li et al.,
        // Sound absorption by tree bark, Applied Acoustics 2020: 0.04-0.05 below 1 kHz, rising but
        // under 0.1 to 1.6 kHz, the top of what they measured), the natural ground by its Nord2000
        // class (GroundClass). Props have no per-band figure; their single value stands for both.
        private static readonly Dictionary<int, Vector2> Bands = new Dictionary<int, Vector2>();
        private static Vector2 BandsAt(int i)
        {
            if (Alpha[i] == WaterAbsorb) return new Vector2(.008f, .025f);
            Collider c = s_hits[i].collider;
            if (c == null) return new Vector2(Alpha[i], Alpha[i]);
            int id = c.GetInstanceID();
            if (!Bands.TryGetValue(id, out Vector2 b))
            {
                if (Bands.Count > 8192) Bands.Clear();
                WearNTear wnt = c.GetComponentInParent<WearNTear>();
                if (wnt != null && Structural(c))
                    switch (wnt.m_materialType)
                    {
                        case WearNTear.MaterialType.Stone: case WearNTear.MaterialType.Ashstone: b = new Vector2(.03f, .07f); break;
                        case WearNTear.MaterialType.Marble: b = new Vector2(.01f, .02f); break;
                        case WearNTear.MaterialType.Ice: b = new Vector2(.008f, .025f); break;
                        case WearNTear.MaterialType.HardWood: case WearNTear.MaterialType.Timberwood: case WearNTear.MaterialType.Ancient: b = new Vector2(.04f, .07f); break;
                        case WearNTear.MaterialType.Iron: b = new Vector2(.09f, .11f); break; // steel decking
                        default: b = new Vector2(.11f, .07f); break;
                    }
                else if (wnt != null) { Vector3 f = Furniture(c, wnt); b = new Vector2(f.x, f.z); }
                else if (c.GetComponentInParent<Heightmap>() != null) b = new Vector2(-2f, -2f); // ground: paved or not, by the spot
                else if (IsTree(c)) b = new Vector2(.05f, .1f); // bark
                else if (c.gameObject.layer == LayerMask.NameToLayer("static_solid")) b = new Vector2(.02f, .05f); // limestone wall
                else b = new Vector2(-1f, -1f);
                Bands[id] = b;
            }
            if (b.x == -2f)
            {
                Heightmap hm = Heightmap.FindHeightmap(Point[i]);
                int k = hm != null ? GroundClassAt(hm, Point[i]) : 3;
                return k < 0 ? new Vector2(.01f, .02f) : new Vector2(GroundClass[k].x, GroundClass[k].z);
            }
            return b.x < 0f ? new Vector2(Alpha[i], Alpha[i]) : b;
        }

        // Furniture and clutter at 250 Hz, 500 Hz and 4 kHz (pyroomacoustics' material database):
        // beds as empty cloth-covered upholstered chairs, rugs, hides and furs as hairy carpet on felt,
        // banners, tapestries and curtains as tight velvet curtains, anything else as wooden chairs.
        private static Vector3 Furniture(Collider c, WearNTear wnt)
        {
            Piece p = c.GetComponentInParent<Piece>();
            string n = wnt.gameObject.name.ToLowerInvariant();
            if (p != null && p.m_comfortGroup == Piece.ComfortGroup.Bed) return new Vector3(.60f, .77f, .70f);
            if ((p != null && p.m_comfortGroup == Piece.ComfortGroup.Carpet) || n.Contains("rug") || n.Contains("hide") || n.Contains("fur") || n.Contains("pillow"))
                return new Vector3(.14f, .37f, .25f);
            if ((p != null && p.m_comfortGroup == Piece.ComfortGroup.Banner) || n.Contains("tapestry") || n.Contains("curtain") || n.Contains("cloth"))
                return new Vector3(.12f, .35f, .36f);
            return new Vector3(.08f, .10f, .12f);
        }

        // How much of what a surface reflects it scatters instead of mirroring (Bounces): ODEON's
        // recommended mid-frequency scattering coefficients (Zeng, Lynge and Rindel 2006), the middle
        // of each range: stone walls as brickwork with filled joints, unplastered (0.05-0.1); log walls
        // as brickwork with open joints (0.1-0.2); planks, iron and ice as smooth surfaces in general
        // (0.02-0.05); marble and water as smooth painted concrete (0.005-0.02); furniture as a
        // bookshelf with some books (0.3); the ground, rock and trees as rough structures (0.4-0.5).
        internal static float ScatterAt(int i) => s_hits[i].collider == null ? .0125f : ScatterOf(s_hits[i].collider, Alpha[i]);
        internal static float ScatterOf(Collider c, float alpha)
        {
            if (alpha == WaterAbsorb || c == null) return .0125f;
            WearNTear wnt = c.GetComponentInParent<WearNTear>();
            if (wnt != null && Structural(c))
                switch (wnt.m_materialType)
                {
                    case WearNTear.MaterialType.Stone: case WearNTear.MaterialType.Ashstone: return .075f;
                    case WearNTear.MaterialType.Marble: return .0125f;
                    case WearNTear.MaterialType.Iron: case WearNTear.MaterialType.Ice: return .035f;
                    default: return wnt.gameObject.name.IndexOf("log", System.StringComparison.OrdinalIgnoreCase) >= 0 ? .15f : .035f;
                }
            if (wnt != null) return .3f;
            return .45f;
        }

        // Absorption where a ray landed: the ground by what's at that very spot, anything else by
        // what it is (AbsorptionOf).
        internal static float AbsorptionAt(Collider c, Vector3 p)
        {
            float a = AbsorptionOf(c);
            if (a >= 0f) return a;
            return Ground.TryGetValue(c.GetInstanceID(), out Heightmap hm) && hm != null ? GroundAt(hm, p) : GroundClass[3].y;
        }

        // The natural ground, by the Nord2000 ground classes (effective flow resistivity in kPa s/m2,
        // as listed in d&b's NoizCalc white paper and Bies, Hansen and Howard, Engineering Noise
        // Control, table 5.2): A 12.5 snow or moss, B 31.5 soft forest floor, thick moss, C 80 turf,
        // grass, loose soil, D 200 normal uncompacted ground, field, forest floor, E 500 compacted
        // field and gravel, F 2000 gravel road, G 20000 asphalt, concrete, H 200000 dense asphalt,
        // water. Each turned into random-incidence absorption at 250 Hz, 500 Hz and 4 kHz the way
        // Nord2000 itself does it: Delany and Bazley's impedance for a thick porous ground, locally
        // reacting, averaged over every angle of arrival.
        private static readonly Vector3[] GroundClass =
        {
            new Vector3(.824f, .891f, .923f), // A
            new Vector3(.663f, .791f, .924f), // B
            new Vector3(.459f, .611f, .904f), // C
            new Vector3(.286f, .413f, .824f), // D
            new Vector3(.165f, .251f, .664f), // E
            new Vector3(.066f, .105f, .369f), // F
            new Vector3(.013f, .022f, .091f), // G
            new Vector3(.002f, .004f, .018f), // H
        };

        // Which class the ground is at a spot: first what the player made of it (paved is -1, the
        // concrete and tile row of the per-band table), else what the biome's ground is.
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
            return k < 0 ? .03f : GroundClass[k].y;
        }

        private static float AbsorptionOf(Collider c)
        {
            int id = c.GetInstanceID();
            if (Absorb.TryGetValue(id, out float a)) return a;
            if (Absorb.Count > 8192) { Absorb.Clear(); Ground.Clear(); }
            WearNTear wnt = c.GetComponentInParent<WearNTear>();
            if (wnt != null && !Structural(c))
            {
                // Furniture and clutter: chests, benches, stations, racks, beds, rugs. They break up
                // and swallow the reflections that make an empty room ring; soft ones most (the game's
                // own comfort groups for beds, banners and carpets, plus anything cloth or fur by name).
                Piece p = c.GetComponentInParent<Piece>();
                bool soft = p != null && (p.m_comfortGroup == Piece.ComfortGroup.Bed || p.m_comfortGroup == Piece.ComfortGroup.Banner || p.m_comfortGroup == Piece.ComfortGroup.Carpet);
                if (!soft)
                {
                    string n = wnt.gameObject.name.ToLowerInvariant();
                    soft = n.Contains("rug") || n.Contains("hide") || n.Contains("fur") || n.Contains("cloth") || n.Contains("tapestry") || n.Contains("curtain") || n.Contains("pillow");
                }
                a = Furniture(c, wnt).y;
            }
            else if (wnt != null)
            {
                switch (wnt.m_materialType)
                {
                    case WearNTear.MaterialType.Stone: case WearNTear.MaterialType.Ashstone: a = .03f; break;
                    case WearNTear.MaterialType.Marble: a = .02f; break;
                    case WearNTear.MaterialType.Iron: a = .08f; break; // steel decking at 500 Hz
                    case WearNTear.MaterialType.Ancient: a = .05f; break;
                    case WearNTear.MaterialType.Ice: a = .03f; break;
                    case WearNTear.MaterialType.HardWood: case WearNTear.MaterialType.Timberwood: a = .09f; break;
                    default: a = .12f; break; // wood planks: thin panels absorb the lows
                }
            }
            else if (c.GetComponentInParent<Heightmap>() is Heightmap hm)
            {
                a = -1f; // the ground: by the exact point (AbsorptionAt)
                Ground[id] = hm;
            }
            else if (IsTree(c)) a = .05f;                                                                          // bark at 500 Hz (BandsAt)
            else if (c.gameObject.layer == LayerMask.NameToLayer("static_solid")) a = .03f;                       // rock: limestone wall at 500 Hz (pyroomacoustics database)
            else a = .15f;                                                                                        // props
            Absorb[id] = a;
            return a;
        }
    }
}
