using System;
using System.Threading;
using UnityEngine;

namespace WiresAcoustics
{
    // Pure, single-wall analytic visibility runs on a .NET worker. Only the
    // main thread reads Unity objects, captures transforms, and updates filters.
    // Work is submitted by the existing Directions update; no new frame hook,
    // polling timer, per-source task or gameplay-thread wait is introduced.
    internal static class AnalyticWorker
    {
        private struct Input
        {
            internal Matrix4x4 World, Inverse;
            internal Quaternion Rotation;
            internal Vector3 Size, Center, Scale, Listener, Source;
            internal float Radius, WallLow, WallMid, Strength;
        }
        private struct Meta
        {
            internal AcousticFilter Filter;
            internal BoxCollider Box;
            internal Vector3 Listener, Source, BoundSource;
            internal int Generation;
            internal float WallLow, WallMid, Strength;
        }
        private struct Output
        {
            internal bool Valid, Failed;
            internal float Covered, Bend, Low, High;
            internal Vector3 Edge;
        }
        private static Input[] s_inputs = new Input[128];
        private static Meta[] s_meta = new Meta[128];
        private static Output[] s_outputs = new Output[128];
        private static int s_count;
        // 0 = available, 1 = running, 2 = completed. Volatile publishes
        // the worker's result array before it becomes visible to Unity.
        private static int s_state;
        private static readonly WaitCallback WorkCallback = _ => Work();
        private static bool s_initialized;

        internal static void Request(AcousticFilter f, Vector3 listener, Vector3 source)
        {
            if (Volatile.Read(ref s_state) != 0 || !f.AnalyticActive ||
                f.AnalyticWall == null || !f.Active || !f.Placed ||
                f.Source == null || !f.Source.isPlaying ||
                !Plugin.Enabled.Value || !Plugin.Occlusion.Value) return;
            float strength = AcousticMath.OcclusionStrength;
            if (f.AnalyticPoseValid && f.AnalyticWorldGen == Occlusion.WorldGen &&
                (listener - f.AnalyticLastEar).sqrMagnitude < 1e-10f &&
                (source - f.AnalyticLastSource).sqrMagnitude < 1e-10f &&
                Mathf.Abs(strength - f.AnalyticLastStrength) < 1e-5f &&
                Mathf.Abs(f.AnalyticIndirectGain - f.AnalyticLastIndirect) < 1e-5f)
                return;
            if (Occlusion.ChangedNear(f.AnalyticWorldGen, listener, source, Corners.Reach))
            {
                f.AnalyticWall = null;
                f.AnalyticActive = false;
                f.AnalyticPoseValid = false;
                return;
            }
            BoxCollider wall = f.AnalyticWall;
            if (wall == null || !wall.enabled || !wall.gameObject.activeInHierarchy)
            {
                f.AnalyticActive = false;
                f.AnalyticPoseValid = false;
                return;
            }
            SourceVolume.Projection projection = SourceVolume.Project(f, listener, source);
            if (s_count == s_inputs.Length)
            {
                int n = s_inputs.Length * 2;
                Array.Resize(ref s_inputs, n);
                Array.Resize(ref s_meta, n);
                Array.Resize(ref s_outputs, n);
            }
            Transform t = wall.transform;
            s_inputs[s_count] = new Input
            {
                World = t.localToWorldMatrix, Inverse = t.worldToLocalMatrix,
                Rotation = t.rotation, Size = wall.size, Center = wall.center,
                Scale = t.lossyScale, Listener = listener,
                Source = projection.Center, Radius = projection.Radius,
                WallLow = f.AnalyticLow, WallMid = f.AnalyticMid,
                Strength = strength
            };
            s_meta[s_count] = new Meta
            {
                Filter = f, Box = wall, Listener = listener, Source = source,
                BoundSource = f.AnalyticSource, Generation = f.AnalyticWorldGen,
                WallLow = f.AnalyticLow, WallMid = f.AnalyticMid,
                Strength = strength
            };
            s_count++;
        }

        internal static void Submit()
        {
            if (s_count == 0 || Volatile.Read(ref s_state) != 0) return;
            // Initialize the immutable Fresnel table on Unity's main thread,
            // never on the audio callback or at a random worker startup.
            if (!s_initialized)
            {
                FresnelEdge.Field(0f, out _, out _);
                s_initialized = true;
            }
            Volatile.Write(ref s_state, 1);
            bool queued = false;
            try { queued = ThreadPool.QueueUserWorkItem(WorkCallback); }
            catch (NotSupportedException) { }
            if (!queued)
            {
                Volatile.Write(ref s_state, 0);
                for (int i = 0; i < s_count; i++)
                    AnalyticOcclusion.Refresh(s_meta[i].Filter,
                        s_meta[i].Listener, s_meta[i].Source);
                s_count = 0;
            }
        }

        private static void Work()
        {
            int count = s_count;
            for (int i = 0; i < count; i++)
            {
                try { s_outputs[i] = Compute(s_inputs[i]); }
                catch (Exception) { s_outputs[i] = new Output { Failed = true }; }
            }
            Volatile.Write(ref s_state, 2);
        }

        // This method reads ONLY value-type data. No Collider, Transform,
        // MonoBehaviour, AudioSource, configuration or Unity scene access.
        private static Output Compute(Input s)
        {
            Output result = new Output { Edge = s.Source, Low = 1f, High = 1f };
            Vector3 delta = s.Source - s.Listener;
            float length = delta.magnitude;
            if (length <= 1e-5f || s.Radius <= 0f) return result;
            Vector3 facing = delta / length;
            Vector3 nx = s.Rotation * Vector3.right;
            Vector3 ny = s.Rotation * Vector3.up;
            Vector3 nz = s.Rotation * Vector3.forward;
            float sx = Mathf.Abs(s.Size.x * s.Scale.x);
            float sy = Mathf.Abs(s.Size.y * s.Scale.y);
            float sz = Mathf.Abs(s.Size.z * s.Scale.z);
            float px = Mathf.Abs(Vector3.Dot(facing, nx)) * sy * sz;
            float py = Mathf.Abs(Vector3.Dot(facing, ny)) * sx * sz;
            float pz = Mathf.Abs(Vector3.Dot(facing, nz)) * sx * sy;
            int normal = px >= py && px >= pz ? 0 : py >= pz ? 1 : 2;
            int u = (normal + 1) % 3, v = (normal + 2) % 3;
            Vector3 worldNormal = normal == 0 ? nx : normal == 1 ? ny : nz;
            float denom = Vector3.Dot(delta, worldNormal);
            if (Mathf.Abs(denom) < 1e-5f) return result;
            Vector3 center = s.World.MultiplyPoint3x4(s.Center);
            float t = Vector3.Dot(center - s.Listener, worldNormal) / denom;
            result.Valid = true;
            if (t <= 0f || t >= 1f) return result;
            Vector3 hit = s.Inverse.MultiplyPoint3x4(s.Listener + delta * t) - s.Center;
            float su = Mathf.Abs(s.Scale[u]), sv = Mathf.Abs(s.Scale[v]);
            float projectedRadius = s.Radius * t;
            float ru = projectedRadius / Mathf.Max(1e-5f, su);
            float rv = projectedRadius / Mathf.Max(1e-5f, sv);
            if (ru <= 1e-5f || rv <= 1e-5f) { result.Valid = false; return result; }
            float hu = s.Size[u] * .5f, hv = s.Size[v] * .5f;
            float cu = hit[u], cv = hit[v];
            float covered = AnalyticOcclusion.DiskRectangle(
                (-hu - cu) / ru, (-hv - cv) / rv,
                ( hu - cu) / ru, ( hv - cv) / rv);
            float eu = (hu - Mathf.Abs(cu)) * su;
            float ev = (hv - Mathf.Abs(cv)) * sv;
            float clearance = eu >= 0f && ev >= 0f
                ? -Mathf.Min(eu, ev)
                : Mathf.Sqrt(Mathf.Max(0f, -eu) * Mathf.Max(0f, -eu) +
                             Mathf.Max(0f, -ev) * Mathf.Max(0f, -ev));
            Vector3 edgeLocal = hit;
            if (eu <= ev) edgeLocal[u] = Mathf.Sign(cu == 0f ? 1f : cu) * hu;
            else edgeLocal[v] = Mathf.Sign(cv == 0f ? 1f : cv) * hv;
            edgeLocal[u] = Mathf.Clamp(edgeLocal[u], -hu, hu);
            edgeLocal[v] = Mathf.Clamp(edgeLocal[v], -hv, hv);
            Vector3 edge = s.World.MultiplyPoint3x4(s.Center + edgeLocal);
            float d1 = t * length, d2 = (1f - t) * length;
            float lowThrough = Mathf.Sqrt(Mathf.Clamp01(s.WallLow));
            float midThrough = Mathf.Sqrt(Mathf.Clamp01(s.WallMid));
            float lowAtEdge = covered > 0f
                ? FresnelEdge.Intensity(clearance, d1, d2, 125f, lowThrough) : 1f;
            float midAtEdge = covered > 0f
                ? FresnelEdge.Intensity(clearance, d1, d2, 2000f, midThrough) : 1f;
            float low = 1f - covered * (1f - lowAtEdge);
            float mid = 1f - covered * (1f - midAtEdge);
            float bend = covered * Mathf.Max(0f, lowAtEdge - s.WallLow) /
                         Mathf.Max(1e-6f, low);
            bend *= Mathf.Clamp01(s.Strength);
            if (s.Strength > 0f && Mathf.Abs(s.Strength - 1f) > 1e-5f)
            {
                low = Mathf.Pow(Mathf.Max(1e-8f, low), s.Strength);
                mid = Mathf.Pow(Mathf.Max(1e-8f, mid), s.Strength);
            }
            result.Covered = covered;
            result.Bend = bend;
            result.Low = s.Strength <= 0f ? 1f : low;
            result.High = s.Strength <= 0f ? 1f : Mathf.Min(low, mid);
            result.Edge = edge;
            return result;
        }

        internal static void CompleteReady()
        {
            if (Volatile.Read(ref s_state) != 2) return;
            for (int i = 0; i < s_count; i++)
            {
                Meta m = s_meta[i];
                AcousticFilter f = m.Filter;
                if (f == null || !f.AnalyticActive || !f.Active || !f.Placed ||
                    f.Source == null || !f.Source.isPlaying || f.AnalyticWall != m.Box ||
                    f.AnalyticWorldGen != m.Generation ||
                    // Unrelated world edits must not invalidate a cached
                    // wall solution for this source indefinitely.
                    Occlusion.ChangedNear(m.Generation, m.Listener,
                        m.Source, Corners.Reach) ||
                    f.AnalyticSource != m.BoundSource ||
                    Mathf.Abs(f.AnalyticLow - m.WallLow) > 1e-5f ||
                    Mathf.Abs(f.AnalyticMid - m.WallMid) > 1e-5f ||
                    Mathf.Abs(AcousticMath.OcclusionStrength - m.Strength) > 1e-5f ||
                    (Hearing.At - m.Listener).sqrMagnitude > 1f ||
                    (f.transform.position - m.Source).sqrMagnitude > 1f)
                    continue;
                Output r = s_outputs[i];
                if (r.Failed)
                {
                    // The worker has no Unity API access. If its pure
                    // calculation fails, recover using the proven synchronous
                    // path for this source only, rather than dropping audio.
                    f.AnalyticPoseValid = false;
                    AnalyticOcclusion.Refresh(f, m.Listener, m.Source);
                    continue;
                }
                if (!r.Valid)
                {
                    f.AnalyticPoseValid = false;
                    f.AnalyticActive = false;
                    continue;
                }
                f.AnalyticPoseValid = true;
                // This result is current even if unrelated geometry changed
                // elsewhere while the worker was running.
                f.AnalyticWorldGen = Occlusion.WorldGen;
                f.AnalyticLastEar = m.Listener;
                f.AnalyticLastSource = m.Source;
                f.AnalyticLastStrength = m.Strength;
                f.AnalyticLastIndirect = f.AnalyticIndirectGain;
                f.AnalyticEdgePoint = r.Edge;
                f.AnalyticBendShare = r.Bend;
                f.LastCoverage = r.Covered;
                f.LastOcc = r.Covered;
                SpectralPaths.Decode(r.Low, r.High, out float gain, out float cutoff);
                if (!f.AnalyticHasPortal)
                {
                    float bend = Mathf.Clamp01(r.Bend);
                    f.Routed = bend > .001f;
                    f.RoutePoint = Vector3.Lerp(m.Source, r.Edge, bend);
                }
                if (f.AnalyticIndirectGain > 0f && r.Covered > 0f)
                {
                    float path = f.AnalyticIndirectGain * Mathf.Sqrt(r.Covered);
                    SpectralPaths.Combine(gain, cutoff, path,
                        f.AnalyticIndirectCutoff, out gain, out cutoff);
                }
                if (Mathf.Abs(f.TargetGain - gain) > .0001f ||
                    Mathf.Abs(f.TargetCutoff - cutoff) > 1f)
                    f.Set(gain, cutoff, false);
            }
            s_count = 0;
            Volatile.Write(ref s_state, 0);
        }

        // Flush is only used on an explicit mode change or shutdown, never
        // in the normal frame path. Do not reuse worker-owned arrays early.
        internal static void Flush()
        {
            while (Volatile.Read(ref s_state) == 1) Thread.Yield();
            s_count = 0;
            Volatile.Write(ref s_state, 0);
        }
    }
}
