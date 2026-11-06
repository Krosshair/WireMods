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

        // Measured, smoothed (read by Occlusion for the per-sound reverb send).
        internal static float Enclosure, Absorption = .3f;
        private static float s_path, s_alpha, s_decay;

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
            private static bool s_wasOn, s_wasHrtf;

            private static void Postfix(AudioMan __instance)
            {
                long t0 = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                int gc0 = t0 != 0L ? System.GC.CollectionCount(0) : 0;
                // Switched off while playing: hand every live sound back to the game, finish pending work.
                bool on = Plugin.Enabled.Value, hrtf = on && Plugin.Hrtf.Value;
                if (s_wasHrtf && !hrtf) { Directions.Flush(); AcousticFilter.ReleaseHrtf(); }
                if (s_wasOn && !on) { PortalLoop.Clear(); Travel.Clear(); Batch.Flush(); AcousticFilter.ReleaseAll(); Underwater.Reset(); Follow.Clear(); AcousticFilter.SetEcho(0f, Vector3.forward); }
                s_wasOn = on; s_wasHrtf = hrtf;
                if (!on) { Underwater.Reset(); Directions.Idle(); }
                if (Plugin.Enabled.Value && __instance.GetActiveAudioListener() is AudioListener l)
                {
                    Transform lt = l.transform;
                    // a new listener (the menu's, then the game camera's after loading in) comes with the
                    // game's own reverb zone: removed at once, not at the next safety check
                    if (lt != s_listener) { s_listener = lt; s_nextScan = 0f; }
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
                    float echoScale = Plugin.RoomReverb.Value ? Mathf.Clamp(Plugin.ReverbStrength.Value, 0f, 2f) : 0f;
                    for (int k = 0; k < Bounces.Taps; k++)
                        AcousticFilter.SetEcho(k, Bounces.EchoGains[k] * echoScale, lt.InverseTransformDirection(Bounces.EchoDirs[k]), Bounces.EchoDists[k]);
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
                long rays = Cost.Start();
                Run(__instance);
                Cost.Add(ref Cost.Rays, rays);
                Cost.AddTick(t0, gc0);
            }

            private static void Run(AudioMan __instance)
            {
                // The rays measure the space for the reverb and for muffling ambience: on if either is.
                if (!Plugin.Enabled.Value || (!Plugin.RoomReverb.Value && !Plugin.Occlusion.Value)) { Restore(); return; }
                float now = Time.time;
                if (now < s_next) return;
                s_next = now + Interval;
                AudioListener listener = __instance.GetActiveAudioListener();
                if (listener == null) return;
                Bounces.Complete();
                if (s_pending)
                {
                    s_job.Complete();
                    s_pending = false;
                    long c = Cost.Start();
                    Measure();
                    Cost.Add(ref Cost.Measure, c);
                }
                if (!Plugin.RoomReverb.Value) { RestoreZones(); EnsureArrays(); }
                else Setup(listener.transform);
                Vector3 from = listener.transform.position;
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

        // The game's own zones are removed while the mod is on, for good: they'd add their fixed,
        // distance-only reverb on top of the measured one (a dungeon's on top of the measured room).
        internal static bool RemoveZone(AudioReverbZone z)
        {
            if (z == null || z == s_zone) return false;
            if (Plugin.DebugLog.Value) Plugin.Log.LogInfo("[acoustics] removed the game's reverb zone " + z.name + " (" + z.reverbPreset + ") at " + z.transform.position.ToString("F0"));
            Object.Destroy(z);
            return true;
        }

        private static void RemoveVanilla()
        {
            foreach (AudioReverbZone z in Object.FindObjectsByType<AudioReverbZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                RemoveZone(z);
        }

        // Our reverb off (the game's zones stay removed).
        private static void RestoreZones()
        {
            if (s_zone != null && s_zone.enabled) s_zone.enabled = false;
            s_haveApplied = false;
        }

        private static void Restore()
        {
            RestoreZones();
            if (s_pending) { s_job.Complete(); s_pending = false; }
            if (Enclosure > 0f) Ambience.Reset();
            Enclosure = 0f;
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

        // What each room ray landed on, for the bounces and the echo too (the sea's surface included).
        internal static readonly bool[] Hit = new bool[Rays], Tree = new bool[Rays];
        internal static readonly Vector3[] Point = new Vector3[Rays], Normal = new Vector3[Rays];
        internal static readonly float[] Dist = new float[Rays], Alpha = new float[Rays];

        private static void Measure()
        {
            int level = 0, hits = 0, treeLevel = 0;
            float levelHits = 0f, treePath = 0f;
            float pathSum = 0f, alphaSum = 0f, escaped = 0f;
            float near0 = MaxDistance, near1 = MaxDistance, near2 = MaxDistance;
            for (int i = 0; i < Rays; i++)
            {
                bool hit = Surface(s_from, Dirs[i], s_hits[i], MaxDistance, out Point[i], out Normal[i], out Dist[i], out Alpha[i], out Tree[i]);
                Hit[i] = hit;
                // a tree trunk isn't a wall: a forest only half encloses you (TreeWeight), a swamp
                // full of trunks isn't a cave
                if (Dirs[i].y > -.05f)
                {
                    level++;
                    if (hit && Tree[i]) { levelHits += TreeWeight; treeLevel++; treePath += Dist[i]; }
                    else if (hit) levelHits += 1f;
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
            float decay = Mathf.Clamp(.04f * path / alphaEff, .1f, 8f);
            float nearest = (near0 + near1 + near2) / 3f;

            // The reverberant level, as a real room's: proportional to (1 - a) / a (the room constant), so
            // a bare stone room rings, a furnished one barely does. 0 dB at a = 0.05.
            float reverbLevel = Mathf.Clamp(1000f * Mathf.Log10((1f - alpha) / alpha * .05f / .95f), -3000f, 0f); // millibels
            float wet = Mathf.Clamp01(enclosure * (1f + reverbLevel / 3000f));
            var room = new Params
            {
                Room = Mathf.Lerp(-2600f, -150f + reverbLevel, enclosure),
                RoomHF = Mathf.Lerp(-150f, -2600f, Mathf.Clamp01(alpha * 2.5f)),
                Decay = decay,
                DecayHF = Mathf.Lerp(1.2f, .3f, Mathf.Clamp01(alpha * 2.5f)),
                Reflections = Mathf.Lerp(-2600f, -250f + reverbLevel * .6f, enclosure),
                ReflectionsDelay = Mathf.Clamp(2f * nearest / 343f, .002f, .3f),
                Reverb = Mathf.Lerp(-2200f, 200f + reverbLevel, wet),
                ReverbDelay = Mathf.Clamp(path / 343f, .005f, .1f)
            };
            // A forest's own reverb: trunks all round scatter a sound back and forth before it gets
            // out through the gaps and the canopy. Real forests measure a soft tail of about 1 to 2 s
            // when dense, its highs taken by the needles and leaves, some 10 to 20 dB under the direct
            // sound: here by the share of level rays that hit a trunk. Only where it's louder than
            // what the space already gives (a hall among trees stays the hall).
            float scatter = level > 0 ? (float)treeLevel / level : 0f;
            if (scatter > 0f)
            {
                float s = Mathf.Sqrt(scatter);
                float forestRoom = Mathf.Lerp(-2600f, -1200f, s), forestLate = Mathf.Lerp(-1500f, 0f, s);
                if (forestRoom + forestLate > room.Room + room.Reverb)
                {
                    room.Room = Mathf.Max(room.Room, forestRoom);
                    room.Reverb = Mathf.Max(room.Reverb, forestLate);
                    room.Decay = Mathf.Max(room.Decay, Mathf.Lerp(.5f, 1.8f, scatter));
                    room.DecayHF = Mathf.Min(room.DecayHF, .5f);
                    room.ReverbDelay = Mathf.Clamp(treePath / treeLevel / 343f, .005f, .1f);
                }
            }
            // Out in the open the same: open sky lets the sound out, so it's dry, apart from the
            // ground and what stands round you.
            Params target = room;
            float strength = Plugin.ReverbStrength.Value;
            float gain = strength > .001f ? 2000f * Mathf.Log10(strength) : -10000f; // millibels
            target.Room += gain;
            target.Reverb += gain;
            target.Reflections += gain;

            s_current = s_haveCurrent ? Params.Lerp(s_current, target, Smooth) : target;
            s_haveCurrent = true;
            Enclosure = Mathf.Lerp(Enclosure, enclosure, Smooth);
            Absorption = Mathf.Lerp(Absorption, alpha, Smooth);
            s_path = path; s_alpha = alpha; s_decay = decay;
            // Only when it changed audibly (setting the zone's values is main-thread work).
            if (s_zone != null && Plugin.RoomReverb.Value && (!s_haveApplied || s_current.Differs(s_applied)))
            {
                s_current.ApplyTo(s_zone);
                s_applied = s_current;
                s_haveApplied = true;
            }
            Portals.Find(s_from, Dirs, s_hits, enclosure, path);
            Bounces.Schedule(Dirs, enclosure, s_from);
            if (AudioMan.instance != null) Ambience.Update(AudioMan.instance, Enclosure);

            if (Plugin.DebugLog.Value && Time.time >= s_nextLog)
            {
                s_nextLog = Time.time + 3f;
                Plugin.Log.LogInfo("[acoustics] space: enclosed " + enclosure.ToString("P0") + ", size " + path.ToString("F1") + " m (nearest " +
                    nearest.ToString("F1") + " m), absorption " + alpha.ToString("F2") + " (open " + (escaped / Rays).ToString("P0") + "), RT60 " +
                    decay.ToString("F2") + " s -> " + s_current + "; " + Portals.Summary() + "; " + Bounces.Summary() + "; " + Ocean.Summary() + "; " + Ambience.Summary() + "; " + Spawned.Summary() + ", following their owner " + Follow.Count + "; " + Underwater.Summary() + "; " + Occlusion.Summary() + ", " + Travel.Summary() + "; " + Budget.Summary() + "; " + Budget.Loudness() + "; " + Cost.Report() + "; " + Budget.Toggles());
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
        // the reverb and bounce paths), never echo, and muffle little.
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

        // Absorption where a ray landed: the ground by what's at that very spot, anything else by
        // what it is (AbsorptionOf).
        internal static float AbsorptionAt(Collider c, Vector3 p)
        {
            float a = AbsorptionOf(c);
            if (a >= 0f) return a;
            return Ground.TryGetValue(c.GetInstanceID(), out Heightmap hm) && hm != null ? GroundAt(hm, p) : .35f;
        }

        // The ground's absorption (mid frequencies, typical measured values): first what the player
        // made of it (paved, tilled, dug), else what the biome's ground is.
        private static float GroundAt(Heightmap hm, Vector3 p)
        {
            Texture2D mask = hm.GetPaintMask();
            if (mask != null)
            {
                try
                {
                    Color c = hm.GetPaintMask(p);
                    if (c.b > .5f) return .03f;  // paved: stone
                    if (c.g > .5f) return .55f;  // tilled: loose soil
                    if (c.r > .5f) return .25f;  // dug or cleared: packed earth
                }
                catch (System.Exception) { }
            }
            switch (hm.GetBiome(p))
            {
                case Heightmap.Biome.Meadows: return .4f;      // grass
                case Heightmap.Biome.BlackForest: return .55f; // forest floor: moss, needles
                case Heightmap.Biome.Swamp: return .15f;       // wet mud and standing water
                case Heightmap.Biome.Mountain: return .7f;     // snow
                case Heightmap.Biome.DeepNorth: return .7f;    // snow
                case Heightmap.Biome.Plains: return .35f;      // dry grass
                case Heightmap.Biome.Mistlands: return .3f;    // rock and moss
                case Heightmap.Biome.AshLands: return .25f;    // rock and ash
                case Heightmap.Biome.Ocean: return .3f;        // sand
                default: return .35f;
            }
        }

        // What an object is, wherever it came from (built, a location's, the world's): its material
        // and its shape decide, nothing else.
        //  - Shape: a big surface (a wall, floor, cliff, boulder) reflects as its material does; a
        //    small object (furniture, a crate, a stone on the ground) breaks up the reflections
        //    around it and swallows more, soft ones (beds, rugs, hides, cloth) the most. Building
        //    pieces by their build category (Structural), anything else by size (1.8 m).
        //  - Material: building pieces and the world's ruins and towers say theirs (stone, marble,
        //    iron, wood); rock is rock (mineable rock, the static rock layer); anything else counts as
        //    a generic prop.
        private static float AbsorptionOf(Collider c)
        {
            int id = c.GetInstanceID();
            if (Absorb.TryGetValue(id, out float a)) return a;
            if (Absorb.Count > 8192) { Absorb.Clear(); Ground.Clear(); }
            if (c.GetComponentInParent<Heightmap>() is Heightmap hm)
            {
                a = -1f; // the ground: by the exact point (AbsorptionAt)
                Ground[id] = hm;
            }
            else if (IsTree(c)) a = .8f; // trees: sound mostly passes and scatters
            else
            {
                WearNTear wnt = c.GetComponentInParent<WearNTear>();
                Vector3 size = c.bounds.size;
                bool small = wnt != null ? !Structural(c) : Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < 1.8f;
                if (small && c.GetComponentInParent<Character>() == null) a = Soft(c, wnt) ? .55f : .3f;
                else a = Material(c, wnt);
            }
            Absorb[id] = a;
            return a;
        }

        private static float Material(Collider c, WearNTear wnt)
        {
            if (wnt != null)
            {
                switch (wnt.m_materialType)
                {
                    case WearNTear.MaterialType.Stone: case WearNTear.MaterialType.Ashstone: return .03f;
                    case WearNTear.MaterialType.Marble: return .02f;
                    case WearNTear.MaterialType.Iron: return .04f;
                    case WearNTear.MaterialType.Ancient: return .05f;
                    case WearNTear.MaterialType.Ice: return .03f;
                    case WearNTear.MaterialType.HardWood: case WearNTear.MaterialType.Timberwood: return .09f;
                    default: return .12f; // wood planks: thin panels absorb the lows
                }
            }
            if (c.gameObject.layer == LayerMask.NameToLayer("static_solid") || c.GetComponentInParent<MineRock>() != null || c.GetComponentInParent<MineRock5>() != null)
                return .04f; // rock
            return .15f; // props
        }

        // Soft things: beds, banners and carpets by the game's own comfort groups, else cloth, fur,
        // hides and the like by name.
        private static bool Soft(Collider c, WearNTear wnt)
        {
            Piece p = c.GetComponentInParent<Piece>();
            if (p != null && (p.m_comfortGroup == Piece.ComfortGroup.Bed || p.m_comfortGroup == Piece.ComfortGroup.Banner || p.m_comfortGroup == Piece.ComfortGroup.Carpet)) return true;
            string n = (wnt != null ? wnt.gameObject : c.transform.root.gameObject).name.ToLowerInvariant();
            return n.Contains("bed") || n.Contains("rug") || n.Contains("hide") || n.Contains("fur") || n.Contains("cloth") || n.Contains("tapestry") ||
                n.Contains("curtain") || n.Contains("pillow") || n.Contains("banner") || n.Contains("carpet");
        }
    }
}
