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
    // Drives one reverb zone that follows the listener; the game's own zones are switched off while
    // this runs and their settings are the outdoor end of the blend.
    internal static class Room
    {
        private const int Rays = 64;
        private const float MaxDistance = 50f, Interval = .2f, Smooth = .35f, RescanEvery = 5f;
        private static readonly Vector3[] Dirs = BuildDirs();
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending, s_haveCurrent;
        private static float s_next, s_nextScan, s_nextLog, s_nextVoice, s_nextRecheck, s_nextOthers;
        private static Vector3 s_from;
        private static int s_mask;
        private static AudioReverbZone s_zone;
        private static Transform s_zoneParent;
        private static readonly List<AudioReverbZone> Vanilla = new List<AudioReverbZone>();
        private static Params s_outdoor = Params.Plain, s_current, s_applied;
        private static bool s_haveApplied;
        private static bool s_haveVanilla;
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
            internal static Params Plain => new Params { Room = -1600f, RoomHF = -600f, Decay = .5f, DecayHF = .8f, Reflections = -2000f, ReflectionsDelay = .01f, Reverb = -1800f, ReverbDelay = .02f };

            internal static Params From(AudioReverbZone z) => new Params
            {
                Room = z.room, RoomHF = z.roomHF, Decay = z.decayTime, DecayHF = z.decayHFRatio, Reflections = z.reflections,
                ReflectionsDelay = z.reflectionsDelay, Reverb = z.reverb, ReverbDelay = z.reverbDelay
            };

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
                // Switched off while playing: hand every live sound back to the game, finish pending work.
                bool on = Plugin.Enabled.Value, hrtf = on && Plugin.Hrtf.Value;
                if (s_wasHrtf && !hrtf) { Directions.Flush(); AcousticFilter.ReleaseHrtf(); }
                if (s_wasOn && !on) { Batch.Flush(); AcousticFilter.ReleaseAll(); Underwater.Reset(); }
                s_wasOn = on; s_wasHrtf = hrtf;
                if (!on) Underwater.Reset();
                if (Plugin.Enabled.Value && __instance.GetActiveAudioListener() is AudioListener l)
                {
                    Transform lt = l.transform;
                    Underwater.Update(lt.position, Time.deltaTime);
                    Directions.Step(lt);
                    Ambience.Pan(__instance, lt, Time.deltaTime);
                    float t = Time.time;
                    if (t >= s_nextVoice) { s_nextVoice = t + Interval; Occlusion.Voices(lt.position); }
                    if (t >= s_nextRecheck) { s_nextRecheck = t + .5f; Occlusion.Recheck(lt.position); }
                    if (t >= s_nextOthers) { s_nextOthers = t + Others.Every; Others.Scan(__instance); }
                    Batch.Step(lt.position);
                }
                Run(__instance);
                if (t0 != 0L) Cost.Add(ref Cost.Tick, t0);
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
                if (s_pending)
                {
                    s_job.Complete();
                    s_pending = false;
                    Measure();
                }
                if (!Plugin.RoomReverb.Value) { RestoreZones(); EnsureArrays(); }
                else Setup(listener.transform);
                if (Plugin.RoomReverb.Value && now >= s_nextScan)
                {
                    s_nextScan = now + RescanEvery;
                    long t0 = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                    TakeOverVanilla();
                    if (t0 != 0L) Cost.Add(ref Cost.Scan, t0);
                }
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

        // The game's own zones are switched off (they'd add their distance-only reverb on top); the
        // first one's settings are what "outdoors" sounds like.
        private static void TakeOverVanilla()
        {
            foreach (AudioReverbZone z in Object.FindObjectsByType<AudioReverbZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (z == s_zone || !z.enabled) continue;
                if (!s_haveVanilla)
                {
                    s_haveVanilla = true;
                    s_outdoor = Params.From(z);
                    Plugin.Log.LogInfo("[acoustics] game reverb zone " + z.name + " (" + z.reverbPreset + ", " + z.minDistance.ToString("F0") + "-" +
                        z.maxDistance.ToString("F0") + " m): " + s_outdoor + "; used as the outdoor sound");
                }
                z.enabled = false;
                Vanilla.Add(z);
            }
        }

        // The game's reverb back (our zone off, its zones on).
        private static void RestoreZones()
        {
            if (s_zone != null && s_zone.enabled) s_zone.enabled = false;
            foreach (AudioReverbZone z in Vanilla) if (z != null) z.enabled = true;
            Vanilla.Clear();
            s_nextScan = 0f;
            s_haveApplied = false;
        }

        private static void Restore()
        {
            RestoreZones();
            if (s_pending) { s_job.Complete(); s_pending = false; }
            if (Enclosure > 0f) Ambience.Reset();
            Enclosure = 0f;
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

        private static void Measure()
        {
            int level = 0, levelHits = 0, hits = 0;
            float pathSum = 0f, alphaSum = 0f, escaped = 0f;
            float near0 = MaxDistance, near1 = MaxDistance, near2 = MaxDistance;
            for (int i = 0; i < Rays; i++)
            {
                RaycastHit h = s_hits[i];
                bool hit = h.collider != null;
                if (Dirs[i].y > -.05f) { level++; if (hit) levelHits++; }
                if (!hit) { escaped++; continue; }
                hits++;
                pathSum += h.distance;
                alphaSum += AbsorptionOf(h.collider);
                float d = h.distance;
                if (d < near0) { near2 = near1; near1 = near0; near0 = d; }
                else if (d < near1) { near2 = near1; near1 = d; }
                else if (d < near2) near2 = d;
            }
            float enclosure = level > 0 ? (float)levelHits / level : 0f;
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
            // Out in the open the game's own sound stays; the more enclosed, the more it's the room.
            Params target = Params.Lerp(s_outdoor, room, Mathf.Sqrt(enclosure));
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
            if (AudioMan.instance != null) Ambience.Update(AudioMan.instance, Enclosure);

            if (Plugin.DebugLog.Value && Time.time >= s_nextLog)
            {
                s_nextLog = Time.time + 3f;
                Plugin.Log.LogInfo("[acoustics] space: enclosed " + enclosure.ToString("P0") + ", size " + path.ToString("F1") + " m (nearest " +
                    nearest.ToString("F1") + " m), absorption " + alpha.ToString("F2") + " (open " + (escaped / Rays).ToString("P0") + "), RT60 " +
                    decay.ToString("F2") + " s -> " + s_current + "; " + Portals.Summary() + "; " + Ocean.Summary() + "; " + Ambience.Summary() + "; " + Others.Summary() + "; " + Underwater.Summary() + "; " + Occlusion.Summary() + "; " + Cost.Report());
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

        // Absorption coefficient (share of sound energy a surface swallows, mid frequencies) by what the
        // ray hit. Cached per collider.
        private static float AbsorptionOf(Collider c)
        {
            int id = c.GetInstanceID();
            if (Absorb.TryGetValue(id, out float a)) return a;
            if (Absorb.Count > 8192) Absorb.Clear();
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
                a = soft ? .55f : .3f;
            }
            else if (wnt != null)
            {
                switch (wnt.m_materialType)
                {
                    case WearNTear.MaterialType.Stone: case WearNTear.MaterialType.Ashstone: a = .03f; break;
                    case WearNTear.MaterialType.Marble: a = .02f; break;
                    case WearNTear.MaterialType.Iron: a = .04f; break;
                    case WearNTear.MaterialType.Ancient: a = .05f; break;
                    case WearNTear.MaterialType.Ice: a = .03f; break;
                    case WearNTear.MaterialType.HardWood: case WearNTear.MaterialType.Timberwood: a = .09f; break;
                    default: a = .12f; break; // wood planks: thin panels absorb the lows
                }
            }
            else if (c.GetComponent<Heightmap>() != null || c.GetComponentInParent<Heightmap>() != null) a = .35f; // soil, grass
            else if (c.gameObject.layer == LayerMask.NameToLayer("static_solid")) a = .04f;                       // rock
            else a = .15f;                                                                                        // trees, props
            Absorb[id] = a;
            return a;
        }
    }
}
