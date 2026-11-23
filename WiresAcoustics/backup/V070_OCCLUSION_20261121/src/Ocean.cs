using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // The sea, heard from the sea. Vanilla plays the ocean loop at the average centre of the ocean
    // zones (64 m squares) around you, 75% placed and spread wide, so it's mostly "everywhere". Here,
    // once a second, rays straight down at 96 points around the listener (16 directions, 10 to 110 m,
    // on worker threads, read on the next update) find where the water starts (ground under sea
    // level); the loop moves to the nearest shoreline, where the waves break (each direction's first
    // water point, weighted by 1 / distance squared, a metre above the surface). It then gets what
    // every placed sound gets the HRTF and the distance (a shoreline: 3 dB per doubling); what's in
    // the way is worked out for the whole shore, like the wind (Shade). Vanilla still decides its
    // volume (wind, whether there's ocean nearby at all).
    // (It was placed at the first water 6 m deep: measured in game, that put it 80 to 105 m out,
    // past most of its range, with the beach in the way: 7.7 dB quieter and dull.)
    [HarmonyPatch(typeof(AudioMan), "UpdateOceanAmbiance")]
    internal static class Ocean
    {
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> Source = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_oceanAmbientSource");
        private static readonly float[] Radii = { 10f, 20f, 35f, 55f, 80f, 110f };
        // Shoreline arrivals remain distributed. These are measurements,
        // not a fixed set of artificial audio emitters.
        private static readonly Vector3[] ShoreDir = new Vector3[16];
        private static readonly float[] ShoreWeight = new float[16];
        private const int Dirs = 16, Height = 300;
        private const float Interval = 1f, Wet = .3f, Surf = 1f, Follow = .05f;
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending, s_have;
        private static float s_next, s_nextCheck, s_lastCast;
        private static Vector3 s_lastCastPosition;
        private static bool s_everCast;
        private static int s_skippedStationary;
        private static Vector3 s_from, s_point;
        private static int s_mask;
        // Zone coordinates can repeat in different worlds. Cached sea-zone
        // classifications belong to the ZoneSystem that produced them.
        private static ZoneSystem s_zoneSystem;

        private static void Postfix(AudioMan __instance)
        {
            AudioSource src = Source(__instance);
            if (!Plugin.Enabled.Value || src == null) { Shutdown(); return; }
            AudioListener l = __instance.GetActiveAudioListener();
            if (l == null || ZoneSystem.instance == null) return;
            if (!ReferenceEquals(s_zoneSystem, ZoneSystem.instance))
            {
                if (s_pending) { s_job.Complete(); s_pending = false; }
                s_zoneSystem = ZoneSystem.instance;
                SeaZones.Clear();
                s_have = false; s_everCast = false; s_next = 0f;
                RestoreUnlocated(src);
            }
            if (s_pending)
            {
                s_job.Complete(); s_pending = false;
                bool wasLocated = s_have;
                Read();
                if (wasLocated && !s_have) RestoreUnlocated(src);
            }
            float now = Time.time;
            if (now >= s_next)
            {
                s_next = now + Interval;
                bool audible = src.isPlaying && src.volume > .001f;
                bool moved = !s_everCast ||
                    (l.transform.position - s_lastCastPosition).sqrMagnitude > 1.5f * 1.5f;
                if (audible && (moved || now - s_lastCast >= 4f))
                {
                    Cast(l.transform.position);
                    s_lastCast = now; s_lastCastPosition = l.transform.position;
                    s_everCast = true;
                }
                else s_skippedStationary++;
            }
            if (s_have && now >= s_nextCheck && src.isPlaying && src.volume > .001f)
            {
                // the walls and openings between you and the sea, 4 times a second: stepping on and off
                // a wall changes them quickly
                s_nextCheck = now + .25f;
                AcousticFilter f = AcousticFilter.For(src);
                f.Kind = AcousticFilter.Kinds.Ocean;
                f.Placed = true;
                f.Physical(Plugin.RealDistance.Value, true); // the shore is a line
                bool hrtf = Plugin.Hrtf.Value;
                if (hrtf) f.TakePanning(src.spread);
                f.Hrtf = hrtf;
                f.Width = Mathf.Max(f.BaseWidth, Around); // the sea all round you: from all round
                Budget.Note(f, (src.transform.position - l.transform.position).magnitude);
                Shade(f, l.transform.position, src.transform.position);
            }
            // after vanilla's own move toward its zone average: toward the real shore instead
            if (s_have) src.transform.position = Vector3.Lerp(src.transform.position, s_point, Follow);
        }

        // No shoreline was found in the current measurement. Stop steering
        // the HRTF toward the last known shore and return the original
        // source rolloff while vanilla continues managing the ocean sound.
        // Do not stop/restart playback or create a new AudioSource.
        private static void RestoreUnlocated(AudioSource src)
        {
            if (src == null) return;
            AcousticFilter f = src.GetComponent<AcousticFilter>();
            if (f == null || f.Kind != AcousticFilter.Kinds.Ocean) return;
            f.Routed = false;
            f.RoutePoint = Vector3.zero;
            f.Width = f.BaseWidth;
            f.Set(1f, 22000f, false);
            f.Physical(false);
            s_exposure = 1f;
        }

        // What's between you and the sea. The sea is a whole shoreline, not a point: one ray to the
        // middle of it was blocked by a single post. Worked out like the wind (Room.Exposure): how much
        // of the view toward the sea (and up over things) is open; a wall or a house that side still
        // shuts most of it out, a post or a tree barely does. Sheltered entirely, it's as muffled as
        // through one wall (Occlusion: plank wall, 15 dB, 318 Hz). Heard from between the shore's
        // direction and the way that's open to it.
        private const float Sheltered = .18f;
        private static float s_exposure = 1f;
        private static void Shade(AcousticFilter f, Vector3 listener, Vector3 sea)
        {
            Vector3 d = sea - listener;
            float dist = d.magnitude;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            if (dist < .1f || flat.sqrMagnitude < 1e-4f) { f.Set(1f, 22000f, false); f.Routed = false; return; }
            flat.Normalize();
            // The sea can be visible on several sides. An isolated post
            // toward the weighted-average bearing must not muffle the
            // entire ocean. Evaluate how sheltered EACH observed shoreline
            // arrival is, reusing the room's already measured rays.
            Vector3 way = Vector3.zero;
            float open = 0f, total = 0f;
            for (int i = 0; i < Dirs; i++)
            {
                float weight = ShoreWeight[i];
                if (weight <= 0f) continue;
                float exposure = Room.Exposure(ShoreDir[i], out Vector3 via);
                open += exposure * weight;
                total += weight;
                way += (via.sqrMagnitude > 1e-6f ? via : ShoreDir[i]) *
                    (exposure * weight);
            }
            if (total > 1e-8f)
            {
                open /= total;
                if (way.sqrMagnitude > 1e-6f) way.Normalize();
            }
            else open = Room.Exposure(flat, out way);
            // Room rays are updated asynchronously; smooth their response
            // at the existing shade-update cadence.
            s_exposure = Mathf.Lerp(s_exposure, open, .5f);
            float s = AcousticMath.OcclusionStrength;
            float k = Mathf.Min(1f, s);
            float gain = Mathf.Lerp(1f, Mathf.Lerp(Sheltered, 1f, s_exposure), k);
            float cut = AcousticMath.LogLerp(22000f, Mathf.Lerp(Occlusion.WallCut, 22000f, s_exposure), k);
            f.Set(gain, cut, false);
            f.Routed = way.sqrMagnitude > 0f;
            f.RoutePoint = listener + Vector3.Slerp(d / dist, way.sqrMagnitude > 0f ? way : d / dist, .5f) * dist;
        }

        private static void Cast(Vector3 from)
        {
            RayJobs.EnsureFixed(ref s_cmds, ref s_hits, Dirs * Radii.Length);
            if (s_mask == 0) s_mask = LayerMask.GetMask("terrain");
            s_from = from;
            var q = RayJobs.Query(s_mask);
            float top = ZoneSystem.instance.m_waterLevel + Height;
            for (int d = 0, i = 0; d < Dirs; d++)
            {
                float a = d * Mathf.PI * 2f / Dirs;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                foreach (float r in Radii)
                {
                    Vector3 p = from + dir * r;
                    s_cmds[i++] = new RaycastCommand(new Vector3(p.x, top, p.z), Vector3.down, q, Height * 2f);
                }
            }
            s_job = RayJobs.Submit(s_cmds, s_hits, 16, 1);
            s_pending = true;
        }

        // Each direction's first water that is the sea, weighted by 1 / distance squared: the way the
        // shore lies (the directions added as arrows) and how far (their distances averaged). Added as
        // points instead, water on two sides cancelled out and put the sea under you (an island, a
        // point, a pier). How much the arrows cancel is how much the sea is all round you: the sound
        // gets that wide (Around).
        private static void Read()
        {
            float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            Vector3 way = Vector3.zero, nearestDir = Vector3.zero;
            float weight = 0f, dist = 0f, nearest = float.MaxValue;
            for (int d = 0; d < Dirs; d++) ShoreWeight[d] = 0f;
            for (int d = 0; d < Dirs; d++)
                for (int k = 0; k < Radii.Length; k++)
                {
                    RaycastHit h = s_hits[d * Radii.Length + k];
                    if (h.colliderInstanceID == 0 || h.point.y > sea - Wet || !SeaZone(h.point)) continue;
                    Vector3 flat = new Vector3(h.point.x - s_from.x, 0f, h.point.z - s_from.z);
                    float r = flat.magnitude;
                    if (r < .01f) break;
                    float w = 1f / (Radii[k] * Radii[k]);
                    ShoreDir[d] = flat / r;
                    ShoreWeight[d] = w;
                    way += flat / r * w;
                    dist += r * w;
                    weight += w;
                    if (r < nearest) { nearest = r; nearestDir = flat / r; }
                    break; // the first sea point in this direction
                }
            s_have = weight > 0f;
            if (!s_have) return;
            float pull = way.magnitude / weight; // 1: all one side, 0: all round you
            Vector3 dir = way.sqrMagnitude > 1e-8f ? way.normalized : nearestDir;
            Vector3 p = s_from + dir * (dist / weight);
            s_point = new Vector3(p.x, sea + Surf, p.z);
            Around = 1f - pull;
        }

        // How much the sea surrounds you (0 one side, 1 all round): how wide it sounds.
        internal static float Around;

        // The sea as the game itself has it for this sound (AudioMan.IsOceanZone): a zone whose middle
        // lies deeper under the sea's surface than its m_oceanDepthTreshold. Rivers and ponds are at
        // sea level too, but not that deep across a whole zone. Kept per zone (the ground never moves).
        private static readonly System.Collections.Generic.Dictionary<Vector2s, bool> SeaZones = new System.Collections.Generic.Dictionary<Vector2s, bool>();
        private static bool SeaZone(Vector3 p)
        {
            ZoneSystem zs = ZoneSystem.instance;
            AudioMan man = AudioMan.instance;
            if (zs == null || man == null) return true;
            Vector2s zone = ZoneSystem.GetZone(p);
            if (SeaZones.TryGetValue(zone, out bool sea)) return sea;
            if (SeaZones.Count > 4096) SeaZones.Clear();
            sea = zs.m_waterLevel - zs.GetGroundHeight(ZoneSystem.GetZonePos(zone)) > man.m_oceanDepthTreshold;
            SeaZones[zone] = sea;
            return sea;
        }

        internal static void Shutdown()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            s_have = false; s_everCast = false; s_next = 0f;
            s_nextCheck = 0f;
            s_zoneSystem = null;
            SeaZones.Clear();
            Around = 0f; s_exposure = 1f;
            for (int i = 0; i < Dirs; i++) ShoreWeight[i] = 0f;
        }

        internal static void Dispose()
        {
            Shutdown();
            RayJobs.Dispose(ref s_cmds, ref s_hits);
        }

        internal static string Summary()
        {
            string status = s_have ? "sea heard from " + (s_point - s_from).magnitude.ToString("F0") + " m" : "no sea near";
            if (Plugin.DebugLog.Value) status += " (shore scans skipped " + s_skippedStationary + ")";
            s_skippedStationary = 0;
            return status;
        }
    }
}
