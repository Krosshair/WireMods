using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // A shoreline is a region of world-space water, not a single position
    // synthesized anew from the listener's moving 96-ray grid every second.
    // Group contiguous water directions into persistent regions. Up to four
    // independent, world-anchored copies carry directional waves. The native
    // ocean source stays a quiet nondirectional baseline while regions are
    // known, and returns smoothly to vanilla when no shoreline is observed.
    // Copies use independent playback phases to avoid coherent buildup.
    [HarmonyPatch(typeof(AudioMan), "UpdateOceanAmbiance")]
    internal static class Ocean
    {
        private static readonly AccessTools.FieldRef<AudioMan, AudioSource> Source =
            AccessTools.FieldRefAccess<AudioMan, AudioSource>("m_oceanAmbientSource");
        private static readonly float[] Radii = { 10f, 20f, 35f, 55f, 80f, 110f };
        private const int Directions = 16, MaxRegions = 4, Height = 300;
        private const float Wet = .3f, Surf = 1f, SampleEvery = 1f;
        private const float MatchRange = 28f, RetireAfter = 2f;
        private const float MaxAnchorMotion = 2.5f, ShelterGain = .18f;

        private sealed class Region
        {
            internal int Id;
            internal Vector3 Position;
            internal float LastObserved, Gain, Exposure = 1f;
            internal bool Valid;
            internal AudioSource Copy;
            internal AcousticFilter Filter;
        }

        private static readonly Region[] Regions = new Region[MaxRegions];
        private static int s_nextId;
        private static NativeArray<RaycastCommand> s_commands;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending;
        private static float s_next, s_lastScan, s_nextShade;
        private static Vector3 s_scanFrom, s_lastScanFrom;
        private static bool s_everScanned;
        private static int s_mask, s_skippedScans;
        private static bool s_openSea;
        // The game's movable ocean source remains only as a diffuse baseline.
        // Directional waves use dedicated world-anchored sources below.
        private static AudioSource s_originalSource;
        private static float s_originalBlend;
        private static float s_bedGain = 1f;
        private static readonly bool[] WetDir = new bool[Directions];
        private static readonly Vector3[] ShorePoint = new Vector3[Directions];
        private static readonly bool[] Assigned = new bool[MaxRegions];
        private static GameObject s_root;

        // We must not feed our divided-up ocean volume into Valheim's next
        // fade calculation. Restore its unscaled value before vanilla updates.
        private static bool s_scaledOriginal;
        private static float s_unscaledOriginal;
        private static AudioSource s_scaledSource;
        private static void Prefix(AudioMan __instance)
        {
            AudioSource original = Source(__instance);
            if (original != null && s_scaledOriginal &&
                original == s_scaledSource)
                original.volume = s_unscaledOriginal;
            s_scaledOriginal = false;
            s_scaledSource = null;
        }

        private static void Postfix(AudioMan __instance)
        {
            AudioSource original = Source(__instance);
            if (!Plugin.Enabled.Value || original == null)
            {
                Shutdown();
                return;
            }
            AudioListener listener = __instance.GetActiveAudioListener();
            if (listener == null || ZoneSystem.instance == null) return;

            if (s_pending && s_job.IsCompleted)
            {
                s_job.Complete();
                s_pending = false;
                Read();
            }

            float now = Time.time;
            if (now >= s_next)
            {
                s_next = now + SampleEvery;
                bool audible = original.isPlaying && original.volume > .001f;
                bool moved = !s_everScanned ||
                    (listener.transform.position - s_lastScanFrom).sqrMagnitude > 2.25f;
                if (!s_pending && audible &&
                    (moved || now - s_lastScan >= 4f))
                {
                    Cast(listener.transform.position);
                    s_lastScan = now;
                    s_lastScanFrom = listener.transform.position;
                    s_everScanned = true;
                }
                else s_skippedScans++;
            }

            UpdateRegions(original, listener.transform.position, now);
        }

        private static void Cast(Vector3 from)
        {
            if (!s_commands.IsCreated)
            {
                s_commands = new NativeArray<RaycastCommand>(
                    Directions * Radii.Length, Allocator.Persistent);
                s_hits = new NativeArray<RaycastHit>(
                    Directions * Radii.Length, Allocator.Persistent);
            }
            if (s_mask == 0) s_mask = LayerMask.GetMask("terrain");
            s_scanFrom = from;
            float top = ZoneSystem.instance.m_waterLevel + Height;
            var query = new QueryParameters(s_mask, false,
                QueryTriggerInteraction.Ignore, false);
            for (int dir = 0, index = 0; dir < Directions; dir++)
            {
                float a = dir * Mathf.PI * 2f / Directions;
                Vector3 v = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                foreach (float r in Radii)
                {
                    Vector3 p = from + v * r;
                    s_commands[index++] = new RaycastCommand(
                        new Vector3(p.x, top, p.z), Vector3.down,
                        query, Height * 2f);
                }
            }
            s_job = RaycastCommand.ScheduleBatch(s_commands, s_hits, 16, 1);
            JobHandle.ScheduleBatchedJobs();
            s_pending = true;
        }

        private static void Read()
        {
            float sea = ZoneSystem.instance != null ?
                ZoneSystem.instance.m_waterLevel : 30f;
            for (int d = 0; d < Directions; d++)
            {
                WetDir[d] = false;
                for (int k = 0; k < Radii.Length; k++)
                {
                    RaycastHit hit = s_hits[d * Radii.Length + k];
                    if (hit.colliderInstanceID == 0 ||
                        hit.point.y > sea - Wet || !SeaZone(hit.point)) continue;
                    WetDir[d] = true;
                    ShorePoint[d] = new Vector3(hit.point.x, sea + Surf, hit.point.z);
                    break;
                }
            }

            System.Array.Clear(Assigned, 0, Assigned.Length);
            int starts = 0;
            int wetCount = 0;
            for (int d = 0; d < Directions; d++)
            {
                if (!WetDir[d]) continue;
                wetCount++;
                if (!WetDir[(d + Directions - 1) % Directions]) starts++;
            }
            // In open water there is no nearby coastline to anchor to:
            // creating four points 10 m from the player would merely make
            // listener-following sounds. Broad ocean should be diffuse.
            s_openSea = wetCount >= Directions - 2;
            if (!s_openSea && starts > 0)
            {
                for (int d = 0; d < Directions; d++)
                {
                    if (!WetDir[d] || WetDir[(d + Directions - 1) % Directions])
                        continue;
                    int length = 0;
                    while (length < Directions && WetDir[(d + length) % Directions])
                        length++;
                    // Large coast arcs become separate spatial regions.
                    int groups = Mathf.CeilToInt(length / 4f);
                    groups = Mathf.Min(groups, MaxRegions);
                    for (int g = 0; g < groups; g++)
                    {
                        int start = d + (length * g) / groups;
                        int stop = d + (length * (g + 1)) / groups;
                        ObserveSegment(start, stop - start, sea);
                    }
                }
            }

            float now = Time.time;
            for (int i = 0; i < MaxRegions; i++)
            {
                Region region = Regions[i];
                if (region != null && region.Valid && !Assigned[i] &&
                    now - region.LastObserved >= RetireAfter)
                    region.Valid = false;
            }
        }

        private static void ObserveSegment(int start, int count, float sea)
        {
            if (count <= 0) return;
            Vector3 center = Vector3.zero;
            int wet = 0;
            for (int j = 0; j < count; j++)
            {
                int d = (start + j) % Directions;
                if (!WetDir[d]) continue;
                center += ShorePoint[d];
                wet++;
            }
            if (wet == 0) return;
            center /= wet;
            center.y = sea + Surf;

            int bestSlot = -1;
            float best = MatchRange * MatchRange;
            for (int i = 0; i < MaxRegions; i++)
            {
                Region r = Regions[i];
                if (Assigned[i] || r == null || !r.Valid) continue;
                float distance2 = (r.Position - center).sqrMagnitude;
                if (distance2 >= best) continue;
                best = distance2;
                bestSlot = i;
            }
            if (bestSlot < 0)
                for (int i = 0; i < MaxRegions; i++)
                {
                    Region r = Regions[i];
                    if (Assigned[i] || r != null && (r.Valid || r.Gain > .003f))
                        continue;
                    bestSlot = i;
                    break;
                }
            if (bestSlot < 0) return;
            Assigned[bestSlot] = true;
            Region region = Regions[bestSlot];
            if (region == null) region = Regions[bestSlot] = new Region();
            if (!region.Valid)
            {
                region.Id = ++s_nextId;
                region.Position = center;
                region.Exposure = 1f;
                region.Gain = 0f;
                region.Valid = true;
            }
            else
            {
                // A physical shoreline region may extend along a beach.
                // Adapt its representative point gradually as more of that
                // same region becomes relevant; never snap to a new centroid.
                region.Position = Vector3.MoveTowards(region.Position, center,
                    MaxAnchorMotion * SampleEvery);
            }
            region.LastObserved = Time.time;
        }

        private static void UpdateRegions(AudioSource original, Vector3 ear, float now)
        {
            if (s_originalSource != original)
            {
                RestoreOriginal();
                s_originalSource = original;
                s_originalBlend = original.spatialBlend;
                s_bedGain = 1f;
            }

            int active = 0;
            for (int i = 0; i < MaxRegions; i++)
                if (Regions[i] != null && Regions[i].Valid) active++;
            float share = 1f / Mathf.Sqrt(Mathf.Max(1, active));
            float gameVolume = original.volume;
            bool shadeDue = now >= s_nextShade;

            for (int i = 0; i < MaxRegions; i++)
            {
                Region region = Regions[i];
                if (region == null) continue;
                float target = region.Valid && original.isPlaying &&
                    gameVolume > .0001f ? share : 0f;
                // Only source arrival and retirement need a short fade.
                // The distance/direction remain sample-continuous in Unity.
                region.Gain = Mathf.MoveTowards(region.Gain, target,
                    Time.deltaTime * 3f);
                if (region.Gain < .002f && !region.Valid)
                {
                    if (region.Copy != null && region.Copy.isPlaying)
                        region.Copy.Stop();
                    continue;
                }
                if (original.clip == null) continue;
                EnsureCopy(region, i, original);
                region.Copy.transform.position = region.Position;
                region.Copy.volume = gameVolume * region.Gain;
                region.Copy.pitch = original.pitch;
                ApplyFilter(region.Copy, region, ear, shadeDue);
            }

            // The native ocean source still follows Valheim's zone average.
            // Do not expose that moving point as a directional emitter.
            // While coast regions are available, keep it as a quiet diffuse
            // ocean floor; if observations fail, restore vanilla gradually.
            s_bedGain = Mathf.MoveTowards(s_bedGain,
                active > 0 ? .14f : 1f, Time.deltaTime * 2f);
            float diffuse = Mathf.Clamp01((1f - s_bedGain) / .86f);
            original.spatialBlend = Mathf.Lerp(s_originalBlend, 0f,
                s_openSea ? 1f : diffuse);
            s_unscaledOriginal = gameVolume;
            s_scaledSource = original;
            s_scaledOriginal = true;
            original.volume = gameVolume * s_bedGain;
            if (shadeDue) s_nextShade = now + .25f;
        }
        private static void EnsureCopy(Region region, int slot, AudioSource game)
        {
            if (s_root == null)
            {
                s_root = new GameObject("WiresAcousticsShorelines");
                Object.DontDestroyOnLoad(s_root);
            }
            if (region.Copy == null)
            {
                GameObject go = new GameObject("shoreline_" + slot);
                go.transform.SetParent(s_root.transform, false);
                region.Copy = go.AddComponent<AudioSource>();
                region.Copy.playOnAwake = false;
                region.Filter = go.AddComponent<AcousticFilter>();
            }
            AudioSource src = region.Copy;
            if (!src.isPlaying || src.clip != game.clip)
            {
                if (game.clip == null) return;
                src.Stop();
                // A clip swap restores authored audio settings before the
                // filter reapplies its line-source distance curve.
                if (region.Filter != null) region.Filter.Physical(false);
                src.clip = game.clip;
                src.loop = true;
                src.outputAudioMixerGroup = game.outputAudioMixerGroup;
                // Direction belongs to the shoreline, never the native
                // listener-relative ocean source's current spatialBlend.
                src.spatialBlend = Mathf.Max(.75f, s_originalBlend);
                src.rolloffMode = game.rolloffMode;
                src.minDistance = game.minDistance;
                src.maxDistance = game.maxDistance;
                AcousticModel.Configure(src, AcousticModel.Shoreline);
                src.dopplerLevel = 0f;
                src.priority = game.priority;
                src.volume = 0f;
                src.Play();
                // The copies represent different wavefronts, not the same
                // coherent waveform at different speaker positions.
                src.timeSamples = UnityEngine.Random.Range(0, game.clip.samples);
            }
        }

        private static void ApplyFilter(AudioSource src, Region region,
            Vector3 ear, bool shadeDue)
        {
            AcousticFilter f = region.Filter;
            if (f == null) f = region.Filter =
                src.GetComponent<AcousticFilter>() ??
                src.gameObject.AddComponent<AcousticFilter>();
            f.Kind = AcousticFilter.Kinds.Ocean;

            if (shadeDue)
            {
                Vector3 toward = region.Position - ear;
                toward.y = 0f;
                if (toward.sqrMagnitude > .01f)
                {
                    Vector3 unused;
                    float open = Room.Exposure(toward.normalized, out unused);
                    region.Exposure = Mathf.Lerp(region.Exposure, open, .5f);
                }
            }
            float strength = Plugin.Occlusion.Value ?
                Mathf.Clamp01(Plugin.OcclusionStrength.Value) : 0f;
            float gain = Mathf.Lerp(1f,
                Mathf.Lerp(ShelterGain, 1f, region.Exposure), strength);
            float cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f),
                Mathf.Log(Mathf.Lerp(Occlusion.WallCut, 22000f,
                    region.Exposure)), strength));
            AcousticModel.Render(f, src, AcousticModel.Shoreline,
                (region.Position - ear).magnitude, gain, cutoff,
                Mathf.Max(f.BaseWidth, .25f));
        }

        private static readonly System.Collections.Generic.Dictionary<Vector2s, bool>
            SeaZones = new System.Collections.Generic.Dictionary<Vector2s, bool>();
        private static bool SeaZone(Vector3 p)
        {
            ZoneSystem zs = ZoneSystem.instance;
            AudioMan man = AudioMan.instance;
            if (zs == null || man == null) return true;
            Vector2s zone = ZoneSystem.GetZone(p);
            if (SeaZones.TryGetValue(zone, out bool sea)) return sea;
            if (SeaZones.Count > 4096) SeaZones.Clear();
            sea = zs.m_waterLevel -
                zs.GetGroundHeight(ZoneSystem.GetZonePos(zone)) >
                man.m_oceanDepthTreshold;
            SeaZones[zone] = sea;
            return sea;
        }

        private static void RestoreOriginal()
        {
            if (s_originalSource != null)
                s_originalSource.spatialBlend = s_originalBlend;
            s_originalSource = null;
        }

        internal static void Shutdown()
        {
            RestoreOriginal();
            if (s_pending) { s_job.Complete(); s_pending = false; }
            if (s_root != null) Object.Destroy(s_root);
            s_root = null;
            for (int i = 0; i < Regions.Length; i++) Regions[i] = null;
            s_everScanned = false;
            s_lastScan = s_next = s_nextShade = 0f;
            s_scaledOriginal = false;
            s_scaledSource = null;
            s_bedGain = 1f;
            s_openSea = false;
            SeaZones.Clear();
        }

        internal static void Dispose()
        {
            Shutdown();
            if (s_commands.IsCreated) s_commands.Dispose();
            if (s_hits.IsCreated) s_hits.Dispose();
            SeaZones.Clear();
        }

        internal static string Summary()
        {
            int count = 0;
            for (int i = 0; i < MaxRegions; i++)
                if (Regions[i] != null && Regions[i].Valid) count++;
            if (!Plugin.DebugLog.Value)
            {
                s_skippedScans = 0;
                return "shoreline regions " + count;
            }
            var summary = new System.Text.StringBuilder("shoreline regions ");
            summary.Append(count).Append(", scans skipped ")
                .Append(s_skippedScans);
            for (int i = 0; i < MaxRegions; i++)
            {
                Region r = Regions[i];
                if (r == null) continue;
                summary.Append(" [").Append(r.Id)
                    .Append(r.Valid ? ":tracked " : ":retiring ")
                    .Append(r.Position.ToString("F1"))
                    .Append(", gain ").Append(r.Gain.ToString("F2"))
                    .Append(", age ").Append((Time.time - r.LastObserved).ToString("F1"))
                    .Append("s]");
            }
            s_skippedScans = 0;
            return summary.ToString();
        }
    }
}
