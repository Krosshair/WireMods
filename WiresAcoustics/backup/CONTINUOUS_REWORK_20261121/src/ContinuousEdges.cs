using UnityEngine;

namespace WiresAcoustics
{
    // Position-dependent propagation from an already discovered, stationary wall edge.
    // No physics query, transform read, allocation, timer, or additional Update hook.
    // Called from the existing direction update, only for currently tracked sounds.
    internal static class ContinuousEdges
    {
        internal static void Measured(AcousticFilter f, in Occlusion.Block wall,
                                      Vector3 listener, Vector3 source)
        {
            // A single verified solid wall is the only case where one cached plane
            // can predict whether the direct path passes around its edge.
            if (wall.Walls == 1 && !wall.Terrain && wall.Soft <= 0f && wall.HasWall)
            {
                Occlusion.Wall(wall, out f.EdgeWallGain, out f.EdgeWallCutoff);
                f.EdgeWallAt = wall.NearAt;
                f.EdgeWallNormal = wall.NearN;
                f.EdgeModelSource = source;
                f.EdgeModelGen = Occlusion.WorldGen;
                f.EdgeModelValid = true;
                f.EdgePositionKnown = false;
            }
            else if (wall.Walls != 0 || wall.Terrain || wall.Soft > 0f)
            {
                f.EdgeModelValid = false;
            }
            // A clear ray does not erase a previously discovered wall edge.
            // The next real geometry change or another obstruction invalidates it.
        }

        internal static bool Evaluate(AcousticFilter f, Vector3 listener, Vector3 source)
        {
            if (!f.EdgeModelValid || !f.EdgeCanOverride || !f.Active || !f.Placed ||
                !Plugin.Enabled.Value || !Plugin.Occlusion.Value ||
                f.Records == null || f.Source == null || !f.Source.isPlaying)
                return false;

            // A moving source requires a fresh path validation. This estimator
            // only extrapolates listener movement around a stationary wall.
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

            // Use a completed, geometry-validated probe belonging to the wall
            // which produced the measured transmission coefficient.
            Corners.Record record = null;
            for (int i = 0; i < f.Records.Length; i++)
            {
                Corners.Record rec = f.Records[i];
                if (rec == null || !rec.Valid || rec.Pending > 0 ||
                    Vector3.Dot(rec.N, f.EdgeWallNormal) < .99f ||
                    !Corners.Covers(rec, f.EdgeWallAt, f.EdgeWallNormal))
                    continue;
                record = rec;
                break;
            }
            if (record == null) return false;

            Vector3 delta = source - listener;
            float along = Vector3.Dot(delta, record.N);
            if (Mathf.Abs(along) < .0001f) return false;
            float t = Vector3.Dot(record.At - listener, record.N) / along;
            if (t <= 0f || t >= 1f) return false;
            Vector3 crossing = listener + delta * t;

            float closest = float.MaxValue;
            Vector3 chosen = Vector3.zero;
            bool illuminated = false;
            bool found = false;
            for (int k = 0; k < Corners.Dirs; k++)
            {
                if (!record.Has[k]) continue;
                Vector3 edge = record.Edges[k];
                float clearance = Vector3.Dot(crossing - edge,
                                               Corners.Dir(record.N, k));
                // Limit extrapolation to the measured neighborhood of an edge.
                if (Mathf.Abs(clearance) > record.Span + Corners.Spacing) continue;
                float distance = Mathf.Abs(clearance);
                if (distance >= closest) continue;
                closest = distance;
                chosen = edge;
                illuminated = clearance >= 0f;
                found = true;
            }
            if (!found) return false;

            float gain, cutoff;
            bool routed = false;
            if (illuminated)
            {
                float detour = Mathf.Max(0f, (chosen - listener).magnitude +
                                            (source - chosen).magnitude - delta.magnitude);
                Diffraction.LitEdge(detour, out gain, out cutoff);
            }
            else
            {
                Diffraction.Round(listener, chosen, source, out float around, out float bendCut);
                around *= Corners.LengthGain(listener, chosen, source);
                // Transmission and diffraction are alternative paths. The
                // stronger is selected here, avoiding unverified energy summing.
                // At the boundary diffraction approaches the same 5 dB loss as
                // the illuminated-side formula.
                routed = around > f.EdgeWallGain;
                gain = routed ? around : f.EdgeWallGain;
                cutoff = routed ? bendCut : f.EdgeWallCutoff;
            }

            if (Underwater.ListenerBelow && Underwater.Muffles(source, f.transform))
                Underwater.Apply(ref gain, ref cutoff);

            f.EdgeLastListener = listener;
            f.EdgeLastSource = source;
            f.EdgePositionKnown = true;
            // Direction changes continuously across the edge as well:
            // diffraction fraction decreases from the shadow into the lit zone.
            float bendWeight = (illuminated || routed) ? Mathf.Clamp01(1f - gain) : 0f;
            f.Routed = bendWeight > .001f;
            f.RoutePoint = Vector3.Lerp(source, chosen, bendWeight);
            f.RouteSpread = 0f;
            if (Mathf.Abs(f.TargetGain - gain) > .001f ||
                Mathf.Abs(f.TargetCutoff - cutoff) > 2f)
            {
                // New geometry is evaluated at the current position. The audio
                // callback still ramps within its buffer to avoid clicks.
                f.FastGeometryResponse = true;
                f.Set(gain, cutoff, false);
            }
            return true;
        }
    }
}
