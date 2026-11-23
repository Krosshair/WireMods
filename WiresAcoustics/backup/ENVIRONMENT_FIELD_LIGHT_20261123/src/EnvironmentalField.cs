using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // A sound's WORLD-SPACE emitting geometry is independent of its audio
    // playback. One 2D Valheim ambience loop receives one aggregated transfer.
    // Point/volume emitters can supply patches to this same path later.
    internal enum EmitterDomain : byte { Surface, Point, Volume }
    internal enum EmitterSurface : byte { Terrain, Roof, Water, Vegetation }

    internal static class EnvironmentalField
    {
        private const int GroundPatches = 12;
        private const int PortalPatches = 2;
        private const int RoofPatches = 2;
        internal const int Capacity = GroundPatches + PortalPatches + RoofPatches;
        private const int Routes = 2;
        private const int MaxHits = Occlusion.MaxHits;
        private const int RaysPerPatch = 1 + 3 + Routes * 6;
        private const float Moved = .45f;

        internal struct Patch
        {
            internal Vector3 At;
            internal float Area;
            internal EmitterSurface Surface;
            internal EmitterDomain Domain;
        }

        private static readonly Patch[] Patches = new Patch[Capacity];
        private static readonly Patch[] Roofs = new Patch[RoofPatches];
        private static readonly Portals.Portal[] Ways = new Portals.Portal[Routes];
        private static int s_roofs, s_count, s_routes, s_world, s_door, s_flightWorld;
        private static Vector3 s_from;
        private static NativeArray<RaycastCommand> s_cmds;
        private static NativeArray<RaycastHit> s_hits;
        private static JobHandle s_job;
        private static bool s_pending, s_ready, s_active;
        private static float s_gain = 1f, s_cutoff = 22000f;
        private static int s_emitting, s_openRoutes, s_jobs;
        internal static bool Ready => s_ready && s_active;
        internal static float Gain => s_gain;
        internal static float Cutoff => s_cutoff;

        // These are ordinary world-space surfaces, not AudioSources.
        // The domain-specific provider decides WHAT emits. The geometry
        // propagation and spectral aggregation below know nothing of rain.
        internal static void BeginSurfaceSurvey() { s_roofs = 0; }

        internal static void OfferRoof(Solids.Info info, RaycastHit hit)
        {
            if (info == null || !info.Roof || info.C == null) return;
            Bounds b = info.C.bounds;
            if (b.size.x < .05f || b.size.z < .05f) return;
            Vector3 at = hit.point;
            at.y = b.max.y + .35f;
            for (int i = 0; i < s_roofs; i++)
                if ((Roofs[i].At - at).sqrMagnitude < 3f) return;
            if (s_roofs >= RoofPatches) return;
            Roofs[s_roofs++] = new Patch {
                At = at, Area = Mathf.Clamp(b.size.x * b.size.z, 1f, 16f),
                Surface = EmitterSurface.Roof, Domain = EmitterDomain.Surface
            };
        }

        // General acoustic input: a set of emitting patches with represented
        // world area. Sampling does not create corresponding audio voices.
        private static void Add(Vector3 at, float area, EmitterSurface material)
        {
            if (s_count >= Capacity) return;
            Patches[s_count++] = new Patch {
                At = at, Area = area, Surface = material,
                Domain = EmitterDomain.Surface
            };
        }

        // 8 near and 4 far terrain patches, world-anchored to a stable 2m
        // grid, then two candidates through known openings and two roofs.
        // The terrain is real Valheim height data, not fake flat ground.
        // Terrain read only when an existing room tick requests a new field.
        private static void Collect(Vector3 listener)
        {
            s_count = 0;
            Vector3 center = new Vector3(
                Mathf.Floor(listener.x * .5f + .5f) * 2f,
                listener.y,
                Mathf.Floor(listener.z * .5f + .5f) * 2f);
            for (int i = 0; i < GroundPatches; i++)
            {
                bool near = i < 8;
                int n = near ? 8 : 4, k = near ? i : i - 8;
                float radius = near ? 5.5f : 17f;
                float a = 2f * Mathf.PI * (k + (near ? .125f : .375f)) / n;
                Vector3 at = center +
                    new Vector3(Mathf.Cos(a) * radius, 0f,
                                Mathf.Sin(a) * radius);
                if (Heightmap.GetHeight(at, out float h))
                {
                    at.y = h + .45f;
                    Add(at, near ? 12f : 60f, EmitterSurface.Terrain);
                }
            }
            for (int i = 0; i < Mathf.Min(PortalPatches, Portals.List.Count); i++)
            {
                Portals.Portal portal = Portals.List[i];
                Vector3 direction = portal.Point - listener;
                direction.y = 0f;
                if (direction.sqrMagnitude < .01f) continue;
                Vector3 at = portal.Point + direction.normalized * 5f;
                if (Heightmap.GetHeight(at, out float h))
                {
                    at.y = h + .45f;
                    Add(at, Mathf.Clamp(portal.Radius * portal.Radius, 2f, 16f),
                        EmitterSurface.Terrain);
                }
            }
            for (int i = 0; i < s_roofs; i++)
                Add(Roofs[i].At, Roofs[i].Area, EmitterSurface.Roof);
            s_routes = Mathf.Min(Routes, Portals.List.Count);
            for (int i = 0; i < s_routes; i++) Ways[i] = Portals.List[i];
        }

        // Called by the EXISTING Room tick (5Hz maximum) rather than any
        // new MonoBehaviour.Update, coroutine, or polling task.
        internal static void Step(Vector3 listener, int mask, bool selected)
        {
            s_active = selected;
            if (s_pending)
            {
                if (!s_job.IsCompleted) return;
                s_job.Complete();
                s_pending = false;
                if (selected && s_flightWorld == Occlusion.WorldGen) Read();
                else s_ready = false;
            }
            if (!selected)
            {
                s_ready = false;
                s_gain = 1f;
                s_cutoff = 22000f;
                return;
            }
            bool changed = s_world != Occlusion.WorldGen ||
                           s_door != Occlusion.DoorGen;
            if (s_ready && !changed &&
                (listener - s_from).sqrMagnitude < Moved * Moved)
                return;
            s_world = Occlusion.WorldGen;
            s_door = Occlusion.DoorGen;
            s_flightWorld = s_world;
            s_from = listener;
            Collect(listener);
            if (s_count == 0) { s_ready = false; return; }

            int count = s_count * RaysPerPatch;
            RayJobs.Ensure(ref s_cmds, ref s_hits, count, MaxHits);
            QueryParameters q = RayJobs.Query(mask);
            int c = 0;
            for (int i = 0; i < s_count; i++)
            {
                Vector3 source = Patches[i].At;
                // A patch only emits surface-impact sound when the sky above
                // its ACTUAL surface is free of a sheltering solid.
                s_cmds[c++] = new RaycastCommand(source + Vector3.up * .15f,
                    Vector3.up, q, 24f);
                RayJobs.Cast3(s_cmds, listener, source, q, ref c);
                for (int k = 0; k < Routes; k++)
                {
                    if (k < s_routes)
                    {
                        RayJobs.Cast3(s_cmds, listener, Ways[k].Point, q, ref c);
                        RayJobs.Cast3(s_cmds, Ways[k].Point, source, q, ref c);
                    }
                    else
                        for (int j = 0; j < 6; j++)
                            s_cmds[c++] = new RaycastCommand(source,
                                Vector3.up, q, 0f);
                }
            }
            s_job = RayJobs.Submit(
                s_cmds.GetSubArray(0, count),
                s_hits.GetSubArray(0, count * MaxHits), 8, MaxHits);
            s_pending = true;
            s_jobs++;
        }

        private static bool Exposed(int first)
        {
            // A ray through leaves does not turn a tree into a watertight
            // ceiling. Any building/rock/terrain shelter blocks rain.
            for (int h = 0; h < MaxHits; h++)
            {
                RaycastHit hit = s_hits[first * MaxHits + h];
                if (hit.colliderInstanceID == 0) break;
                Solids.Info solid = Solids.Of(hit);
                if (solid == null || !solid.Tree) return false;
            }
            return true;
        }

        // The same normalization is used for every emitter domain. This
        // intentionally uses represented area and physical distance; the
        // number of samples never directly multiplies sound level.
        internal static float PatchWeight(float representedArea, float distance2) =>
            Mathf.Max(0f, representedArea) /
            (25f + Mathf.Max(0f, distance2));

        internal static float NormalizeEnergy(float received, float reference) =>
            reference > 1e-8f ?
                Mathf.Clamp01(Mathf.Max(0f, received) / reference) : 0f;

        private static void Read()
        {
            float low = 0f, high = 0f, reference = 0f;
            int contributing = 0, paths = 0;
            Occlusion.Own = null;
            for (int i = 0; i < s_count; i++)
            {
                int first = i * RaysPerPatch;
                Patch patch = Patches[i];
                float d2 = (patch.At - s_from).sqrMagnitude;
                // Normalize against the sampled SURFACE DOMAIN, not only
                // surviving exposed patches: one small hole must not sound
                // as loud as an entire open courtyard. Inactive/sheltered
                // patches contribute zero signal but still occupy area.
                float w = PatchWeight(patch.Area, d2);
                reference += w;
                if (!Exposed(first)) continue;
                Occlusion.Block direct = Occlusion.Block.Clear;
                Batch.DirectWalls(s_cmds, s_hits, first + 1,
                    patch.At, ref direct,
                    out float gain, out float cut, out float covered);
                float energy = gain * gain;
                float treble = SpectralPaths.HighEnergy(gain, cut);
                if (covered > .001f)
                {
                    for (int k = 0; k < s_routes; k++)
                    {
                        int start = first + 4 + k * 6;
                        Occlusion.Block enter = Occlusion.Block.Clear;
                        Occlusion.Block exit = Occlusion.Block.Clear;
                        Batch.DirectWalls(s_cmds, s_hits, start,
                            Ways[k].Point, ref enter,
                            out float ga, out float ca, out _);
                        Batch.DirectWalls(s_cmds, s_hits, start + 3,
                            patch.At, ref exit,
                            out float gb, out float cb, out _);
                        float pg = Batch.PortalTransfer(s_from, patch.At,
                            Ways[k], covered, ga, ca, gb, cb,
                            out float pc);
                        if (pg <= .00001f) continue;
                        energy += pg * pg;
                        treble += SpectralPaths.HighEnergy(pg, pc);
                        paths++;
                    }
                }
                low += w * energy;
                high += w * treble;
                contributing++;
            }
            Occlusion.Own = null;
            s_emitting = contributing;
            s_openRoutes = paths;
            if (reference > 1e-8f)
            {
                SpectralPaths.Decode(
                    NormalizeEnergy(low, reference),
                    NormalizeEnergy(high, reference),
                    out s_gain, out s_cutoff);
            }
            else
            {
                // No sampled outdoor emitting surface: keep the existing
                // game recording active; the next geometry/position survey
                // can recover it. Never delete or restart its AudioSource.
                s_gain = 0f;
                s_cutoff = 400f;
            }
            s_ready = true;
        }

        internal static string Summary() =>
            "emitter field: patches " + s_count +
            ", exposed " + s_emitting +
            ", portal paths " + s_openRoutes +
            ", gain " + s_gain.ToString("F2") +
            ", cutoff " + s_cutoff.ToString("F0") +
            " Hz, jobs " + s_jobs;

        internal static void Reset()
        {
            if (s_pending) { s_job.Complete(); s_pending = false; }
            RayJobs.Dispose(ref s_cmds, ref s_hits);
            s_ready = s_active = false;
            s_gain = 1f;
            s_cutoff = 22000f;
            s_roofs = s_count = s_routes = 0;
            s_world = s_door = -1;
            s_emitting = s_openRoutes = s_jobs = 0;
        }
    }
}
