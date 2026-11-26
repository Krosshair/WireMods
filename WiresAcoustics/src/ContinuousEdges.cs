using UnityEngine;

namespace WiresAcoustics
{
    // Single, measured half-plane edge. Complex diffraction is evaluated
    // from signed Fresnel clearance, not an A/B occlusion flag. Multiple
    // edges, unknown geometry and blocked secondary routes fall back to
    // the existing validated Batch solver; do NOT guess a multipath field.
    internal static class ContinuousEdges
    {
        internal static void Measured(AcousticFilter f, in Occlusion.Block wall,
                                      Vector3 listener, Vector3 source)
        {
            if (wall.Walls == 1 && !wall.Terrain && wall.Soft <= 0f && wall.HasWall)
            {
                Occlusion.Wall(wall, out f.EdgeWallGain, out f.EdgeWallCutoff);
                bool sameWall = f.EdgeModelValid &&
                    Mathf.Abs(Vector3.Dot(f.EdgeWallNormal, wall.NearN)) > .99f &&
                    Mathf.Abs(Vector3.Dot(wall.NearAt - f.EdgeWallAt,
                                           wall.NearN)) < Occlusion.SameWall &&
                    (source - f.EdgeModelSource).sqrMagnitude < .0225f &&
                    !Occlusion.ChangedNear(f.EdgeModelGen, f.EdgeWallAt,
                                           f.EdgeWallAt, Corners.Reach);
                if (!sameWall)
                {
                    f.EdgeWallAt = wall.NearAt;
                    f.EdgeWallNormal = wall.NearN;
                    f.EdgePositionKnown = false;
                }
                f.EdgeModelSource = source;
                f.EdgeModelGen = Occlusion.WorldGen;
                f.EdgeModelValid = true;
            }
            else if (wall.Walls != 0 || wall.Terrain || wall.Soft > 0f)
                f.EdgeModelValid = false;
            // A clear ray does not erase measured stationary wall geometry.
        }

        // A source volume must NOT be replaced by a point-source Fresnel
        // result. Instead integrate the validated single-edge solution
        // over its measured source points. Runs only when a completed
        // Batch raycast is consumed; never in the audio sample callback
        // or a new Update/LateUpdate/FixedUpdate loop.
        internal static bool EvaluateVolume(AcousticFilter f,
            Vector3 listener, Vector3 source,
            Vector3[] sampled, int offset, int sampleCount,
            out float gain, out float cutoff, out Vector3 heard)
        {
            gain = 1f; cutoff = 22000f; heard = source;
            if (sampleCount <= 0 || !f.EdgeModelValid || f.Records == null ||
                (source - f.EdgeModelSource).sqrMagnitude > .0225f ||
                Occlusion.ChangedNear(f.EdgeModelGen, f.EdgeWallAt,
                    f.EdgeWallAt, Corners.Reach)) return false;
            for (int r = 0; r < f.Records.Length; r++)
            {
                Corners.Record record = f.Records[r];
                if (record == null || !record.Valid || record.Pending > 0 ||
                    Mathf.Abs(Vector3.Dot(record.N, f.EdgeWallNormal)) < .99f ||
                    !Corners.Covers(record, f.EdgeWallAt,
                        f.EdgeWallNormal)) continue;
                int count = 0, edgeIndex = -1;
                for (int k = 0; k < Corners.Dirs; k++)
                    if (record.Has[k]) { count++; edgeIndex = k; }
                if (count != 1) continue;
                Vector3 edge = record.Edges[edgeIndex];
                Vector3 openDir = Corners.Dir(record.N, edgeIndex);
                float lowEnergy = 0f, highEnergy = 0f, bending = 0f;
                bool complete = true;
                float span = record.Span + Corners.Spacing;
                for (int i = -1; i < sampleCount; i++)
                {
                    Vector3 point = i < 0 ? source : sampled[offset + i];
                    if (!VolumeFresnel.Transfer(listener, point,
                        record.At, record.N, edge, openDir, span,
                        f.EdgeWallGain, f.EdgeWallCutoff,
                        out float g, out float cut, out float bend))
                    {
                        complete = false;
                        break; // Try the other real wall before giving up.
                    }
                    lowEnergy += g * g;
                    highEnergy += SpectralPaths.HighEnergy(g, cut);
                    bending += bend;
                }
                if (!complete) continue;
                float scale = 1f / (sampleCount + 1f);
                SpectralPaths.Decode(lowEnergy * scale,
                    highEnergy * scale, out gain, out cutoff);
                float w = Mathf.Clamp01(bending * scale) *
                    Corners.LengthGain(listener, edge, source);
                heard = Vector3.Lerp(source, edge, w);
                return true;
            }
            return false;
        }

        internal static bool Evaluate(AcousticFilter f, Vector3 listener, Vector3 source)
        {
            if (!f.EdgeModelValid || !f.EdgeCanOverride || !f.Active || !f.Placed ||
                !Plugin.Enabled.Value || !Plugin.Occlusion.Value ||
                f.Records == null || f.Source == null || !f.Source.isPlaying)
                return false;

            if ((source - f.EdgeModelSource).sqrMagnitude > .0225f ||
                Occlusion.ChangedNear(f.EdgeModelGen, f.EdgeWallAt,
                                      f.EdgeWallAt, Corners.Reach))
            {
                f.EdgeModelValid = false;
                return false;
            }

            if (f.EdgePositionKnown &&
                (listener - f.EdgeLastListener).sqrMagnitude < .000004f &&
                (source - f.EdgeLastSource).sqrMagnitude < .000004f)
                return true;

            Corners.Record record = null;
            for (int i = 0; i < f.Records.Length; i++)
            {
                Corners.Record rec = f.Records[i];
                if (rec == null || !rec.Valid || rec.Pending > 0 ||
                    Mathf.Abs(Vector3.Dot(rec.N, f.EdgeWallNormal)) < .99f ||
                    !Corners.Covers(rec, f.EdgeWallAt, f.EdgeWallNormal))
                    continue;
                record = rec;
                break;
            }
            if (record == null) return false;

            // Only one edge: a single half-plane is the geometry the
            // Fresnel solution actually describes. Two sides of a window
            // cannot be represented by averaging half-plane solutions.
            int edgeCount = 0, edgeIndex = -1;
            for (int k = 0; k < Corners.Dirs; k++)
                if (record.Has[k]) { edgeCount++; edgeIndex = k; }
            if (edgeCount != 1) return false;

            Vector3 direct = source - listener;
            float along = Vector3.Dot(direct, record.N);
            if (Mathf.Abs(along) < .0001f) return false;
            float t = Vector3.Dot(record.At - listener, record.N) / along;
            if (t <= 0f || t >= 1f) return false;

            // Knife-edge Fresnel geometry uses the source/receiver path
            // lengths to the SCREEN intersection, not projections along
            // the screen normal. For an oblique line the latter shrink
            // both distances and artificially strengthen diffraction.
            FresnelEdge.PathDistances(direct.magnitude, t,
                                      out float d1, out float d2);
            if (d1 <= .001f || d2 <= .001f) return false;

            Vector3 edge = record.Edges[edgeIndex];
            Vector3 crossing = listener + direct * t;
            float signed = Vector3.Dot(crossing - edge,
                                       Corners.Dir(record.N, edgeIndex));
            // Don't extrapolate beyond the region actually probed.
            if (Mathf.Abs(signed) > record.Span + Corners.Spacing)
                return false;

            FresnelEdge.Transfer(signed, d1, d2,
                                 f.EdgeWallGain, f.EdgeWallCutoff,
                                 out float gain, out float cutoff, out float bend);

            f.AcousticPath = "single-edge-fresnel";
            f.AcousticRouteCount = 1;
            f.AcousticResolvedAt = Time.time;
            f.AcousticGeneration = Occlusion.WorldGen;
            f.EdgeLastListener = listener;
            f.EdgeLastSource = source;
            f.EdgePositionKnown = true;

            // HRTF localization remains an approximation, not a claimed
            // pressure-field solution. Never pick between arbitrary edges.
            bend *= Corners.LengthGain(listener, edge, source);
            f.Routed = bend > .001f;
            f.RoutePoint = Vector3.Lerp(source, edge, bend);
            f.RouteSpread = 0f;

            if (Plugin.RoomReverb.Value && !f.Source.bypassReverbZones)
            {
                float send = Occlusion.ReverbSend(direct.magnitude, gain);
                if (Mathf.Abs(f.Source.reverbZoneMix - send) > .005f)
                    Occlusion.Send(f, send);
            }
            if (Mathf.Abs(f.TargetGain - gain) > .0005f ||
                Mathf.Abs(f.TargetCutoff - cutoff) > 1f)
            {
                // Ordinary movement around an edge uses the continuous
                // Fresnel response with the standard DSP glide. World-edit
                // hooks separately request the fast response.
                f.Set(gain, cutoff, false);
            }
            return true;
        }
    }
}
