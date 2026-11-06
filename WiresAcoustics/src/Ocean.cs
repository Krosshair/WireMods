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
    // every placed sound gets: muffling by walls, paths through openings (Batch) and the HRTF; the
    // beach itself, right at the water, doesn't hide it (Batch: Shore). Vanilla still decides its
    // volume (wind, whether there's ocean nearby at all).
    // (It was placed at the first water 6 m deep: measured in game, that put it 80 to 105 m out,
    // past most of its range, with the beach in the way: 7.7 dB quieter and dull.)
    [HarmonyPatch(typeof(AudioMan), "UpdateOceanAmbiance")]
    internal static class Ocean
    {
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> Source = AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_oceanAmbientSource");
        private static readonly float[] Radii = { 10f, 20f, 35f, 55f, 80f, 110f };
        private const int Dirs = 16, Height = 300;
        private const float Interval = 1f, Wet = .3f, Surf = 1f, Follow = .05f;
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending, s_have;
        private static float s_next;
        private static Vector3 s_from, s_point;
        private static int s_mask;

        private static void Postfix(AudioMan __instance)
        {
            AudioSource src = Source(__instance);
            if (!Plugin.Enabled.Value || src == null) { Shutdown(); return; }
            AudioListener l = __instance.GetActiveAudioListener();
            if (l == null || ZoneSystem.instance == null) return;
            if (s_pending) { s_job.Complete(); s_pending = false; Read(); }
            float now = Time.time;
            if (now >= s_next)
            {
                s_next = now + Interval;
                Cast(l.transform.position);
                if (s_have && src.isPlaying && src.volume > .001f)
                {
                    AcousticFilter f = src.GetComponent<AcousticFilter>() ?? src.gameObject.AddComponent<AcousticFilter>();
                    f.Kind = AcousticFilter.Kinds.Ocean;
                    f.Placed = true;
                    bool hrtf = Plugin.Hrtf.Value;
                    if (hrtf) f.TakePanning(src.spread);
                    f.Hrtf = hrtf;
                    Budget.Note(f, (src.transform.position - l.transform.position).magnitude);
                    Batch.Enqueue(f, src.transform.position, false, false); // walls and openings, next frame
                }
            }
            // after vanilla's own move toward its zone average: toward the real shore instead
            if (s_have) src.transform.position = Vector3.Lerp(src.transform.position, s_point, Follow);
        }

        private static void Cast(Vector3 from)
        {
            if (!s_cmds.IsCreated)
            {
                s_cmds = new NativeArray<RaycastCommand>(Dirs * Radii.Length, Allocator.Persistent);
                s_hits = new NativeArray<RaycastHit>(Dirs * Radii.Length, Allocator.Persistent);
            }
            if (s_mask == 0) s_mask = LayerMask.GetMask("terrain");
            s_from = from;
            var q = new QueryParameters(s_mask, false, QueryTriggerInteraction.Ignore, false);
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
            s_job = RaycastCommand.ScheduleBatch(s_cmds, s_hits, 16, 1);
            JobHandle.ScheduleBatchedJobs();
            s_pending = true;
        }

        private static void Read()
        {
            float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            Vector3 sum = Vector3.zero;
            float weight = 0f;
            for (int d = 0; d < Dirs; d++)
                for (int k = 0; k < Radii.Length; k++)
                {
                    RaycastHit h = s_hits[d * Radii.Length + k];
                    if (h.collider == null || h.point.y > sea - Wet) continue;
                    float w = 1f / (Radii[k] * Radii[k]);
                    sum += new Vector3(h.point.x, sea + Surf, h.point.z) * w;
                    weight += w;
                    break; // the first deep water point in this direction
                }
            s_have = weight > 0f;
            if (s_have) s_point = sum / weight;
        }

        internal static void Shutdown()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            s_have = false;
        }

        internal static void Dispose()
        {
            Shutdown();
            if (s_cmds.IsCreated) { s_cmds.Dispose(); s_hits.Dispose(); }
        }

        internal static string Summary() => s_have ? "sea heard from " + (s_point - s_from).magnitude.ToString("F0") + " m" : "no sea near";
    }
}
