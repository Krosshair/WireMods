using UnityEngine;

namespace WiresAcoustics
{
    // Geometry is discovered by the existing batched raycasts and corner probes.
    // Sound propagation over an established barrier is then evaluated directly
    // from the current listener/source positions, without an obstruction state machine.
    internal static class ContinuousEdges
    {
        private const float SoundSpeed = 343f;
        private const float LowFrequency = 125f;
        private const float MidFrequency = 2000f;
        private const float EdgeBoundaryEnergy = .316227766f; // 5 dB amplitude loss

        // Convert a low-frequency amplitude and its low-pass to received
        // energies at 125 Hz and 2 kHz. This matches the two controls of the
        // existing DSP; we do not add another filter or change the audio thread.
        private static float MidEnergy(float gain, float cutoff)
        {
            float r = MidFrequency / Mathf.Max(200f, cutoff);
            return gain * gain / (1f + r * r);
        }

        internal static void Spectrum(float lowEnergy, float highEnergy,
                                      out float gain, out float cutoff)
        {
            float low = Mathf.Clamp01(lowEnergy);
            float high = Mathf.Min(low, Mathf.Max(0f, highEnergy));
            gain = Mathf.Sqrt(low);
            if (low < 1e-8f || high >= low * .99999f)
                cutoff = 22000f;
            else
            {
                float ratio = low / Mathf.Max(1e-9f, high) - 1f;
                cutoff = Mathf.Clamp(MidFrequency / Mathf.Sqrt(ratio), 200f, 22000f);
            }
        }

        internal static void Measured(AcousticFilter f, in Occlusion.Block wall,
                                      Vector3 listener, Vector3 source)
        {
            if (wall.Walls == 1 && !wall.Terrain && wall.Soft <= 0f && wall.HasWall)
            {
                Occlusion.Wall(wall, out f.EdgeWallGain, out f.EdgeWallCutoff);
                bool sameWall = f.EdgeModelValid &&
                    Mathf.Abs(Vector3.Dot(f.EdgeWallNormal, wall.NearN)) > .99f &&
                    Mathf.Abs(Vector3.Dot(wall.NearAt - f.EdgeWallAt, wall.NearN)) < Occlusion.SameWall &&
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
            {
                f.EdgeModelValid = false;
            }
            // Retain an established wall when the unobstructed side becomes
            // visible. The edge must not disappear as the direct ray clears it.
        }

        // Pure position-based acoustic transfer. Signed clearance is measured
        // against an actually discovered edge of the wall plane. At zero
        // clearance the illuminated and shadow diffraction calculations both
        // produce the same 5 dB amplitude loss; no binary crossfade remains.
        internal static void Transfer(float signedClearance, float detour,
                                      float wallGain, float wallCutoff,
                                      out float gain, out float cutoff,
                                      out float diffractionShare)
        {
            float d = Mathf.Max(0f, detour);
            float difGain, difCut;
            if (signedClearance >= 0f)
                Diffraction.LitEdge(d, out difGain, out difCut);
            else
                Diffraction.RoundDetour(d, out difGain, out difCut);

            float difEnergy = difGain * difGain;
            float difMid = MidEnergy(difGain, difCut);
            if (signedClearance >= 0f)
            {
                Spectrum(difEnergy, difMid, out gain, out cutoff);
                // Equals one at the shadow boundary and approaches zero in
                // fully illuminated space; both sides have the same limit.
                diffractionShare = Mathf.Clamp01(Mathf.Sqrt(Mathf.Max(0f, 1f - difEnergy) / (1f - EdgeBoundaryEnergy)));
                return;
            }

            // Fresnel-number-dependent wall transmission: the transmitted
            // energy grows as the source enters the barrier's acoustic shadow.
            // At the physical boundary it is zero, so no extra signal can
            // suddenly appear as the listener crosses the edge.
            float fresnel = 2f * d * LowFrequency / SoundSpeed;
            float shadow = 1f - Mathf.Exp(-6f * Mathf.Max(0f, fresnel));
            float wallEnergy = wallGain * wallGain * shadow;
            float wallMid = MidEnergy(wallGain, wallCutoff) * shadow;
            float combined = difEnergy + (1f - difEnergy) * wallEnergy;
            float combinedMid = difMid + (1f - difMid) * wallMid;
            Spectrum(combined, combinedMid, out gain, out cutoff);
            diffractionShare = combined > 1e-8f
                ? Mathf.Clamp01(difEnergy / combined) : 0f;
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

            Vector3 line = source - listener;
            float along = Vector3.Dot(line, record.N);
            if (Mathf.Abs(along) < .0001f) return false;
            float t = Vector3.Dot(record.At - listener, record.N) / along;
            if (t <= 0f || t >= 1f) return false;
            Vector3 crossing = listener + line * t;

            float weightSum = 0f, lowSum = 0f, midSum = 0f;
            float bentSum = 0f;
            Vector3 bendPosition = Vector3.zero;
            float directLength = line.magnitude;
            // Every physically verified edge contributes continuously.
            // A smooth detour kernel replaces "pick the nearest winning edge",
            // which previously changed the heard position abruptly.
            for (int k = 0; k < Corners.Dirs; k++)
            {
                if (!record.Has[k]) continue;
                Vector3 edge = record.Edges[k];
                float clearance = Vector3.Dot(crossing - edge,
                                               Corners.Dir(record.N, k));
                float pathLength = (edge - listener).magnitude + (source - edge).magnitude;
                float detour = Mathf.Max(0f, pathLength - directLength);
                Transfer(clearance, detour, f.EdgeWallGain, f.EdgeWallCutoff,
                         out float eg, out float ec, out float share);
                // At 125 Hz, detours of about a wavelength become weaker
                // contributors, but never suddenly disappear.
                float weight = Mathf.Exp(-2f * Mathf.PI * detour * LowFrequency / SoundSpeed);
                float e = eg * eg;
                weightSum += weight;
                lowSum += weight * e;
                midSum += weight * MidEnergy(eg, ec);
                float bend = weight * share * Corners.LengthGain(listener, edge, source);
                bentSum += bend;
                bendPosition += edge * bend;
            }
            if (weightSum < 1e-8f) return false;
            Spectrum(lowSum / weightSum, midSum / weightSum,
                     out float gain, out float cutoff);
            Underwater.Apply(ref gain, ref cutoff, f.UnderAmount);

            f.EdgeLastListener = listener;
            f.EdgeLastSource = source;
            f.EdgePositionKnown = true;
            // Direction is an energy-weighted spatial field, not one route flag.
            float directionWeight = Mathf.Clamp01(bentSum / weightSum);
            Vector3 averageEdge = bentSum > 1e-8f ? bendPosition / bentSum : source;
            f.Routed = true;
            f.RoutePoint = Vector3.Lerp(source, averageEdge, directionWeight);
            f.RouteSpread = 0f;
            // Reverb must follow the same propagation field as direct sound,
            // not a differently timed binary obstruction result.
            if (Plugin.RoomReverb.Value && !f.Source.bypassReverbZones)
            {
                float send = Occlusion.ReverbSend(directLength, gain);
                if (Mathf.Abs(f.Source.reverbZoneMix - send) > .005f)
                    Occlusion.Send(f, send);
            }

            if (Mathf.Abs(f.TargetGain - gain) > .0005f ||
                Mathf.Abs(f.TargetCutoff - cutoff) > 1f)
            {
                f.FastGeometryResponse = true;
                f.Set(gain, cutoff, false);
            }
            return true;
        }
    }
}
