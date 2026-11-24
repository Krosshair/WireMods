using UnityEngine;

namespace WiresAcoustics
{
    // Distributed exterior sound is an INCIDENT FIELD, not a ring of
    // invented sources around the player. The existing 64-direction
    // room survey already measures how exterior sound reaches the listener:
    // through an opening (unity) or through the actual first boundary
    // material (Steam Audio material transmission).
    //
    // Integrate received LOW and HIGH energy over solid angle. This is
    // the correct compact representation of a dense spatially extended
    // source when playback must stay ONE unspatialized Valheim ambient loop.
    // Weather, biome and other ambience may supply distinct emission
    // domains later; the transfer below itself has no rain-specific rules.
    internal enum EmitterDomain : byte
    {
        ExteriorSurface, AtmosphericVolume, Vegetation, Shoreline
    }

    internal static class EnvironmentalField
    {
        internal static bool Ready { get; private set; }
        internal static float Gain { get; private set; } = 1f;
        internal static float Cutoff { get; private set; } = 22000f;

        private static float s_direct, s_boundary, s_high;
        private static int s_openDirections, s_wallDirections, s_surveys;
        private static EmitterDomain s_domain;

        // A uniform Fibonacci sphere already built by Room is a solid-angle
        // quadrature. Exterior impact surfaces are in the outdoor region;
        // the relevant hemisphere is the horizon and above, with a modest
        // lower sightline to include ground visible through elevated arches.
        // The outdoor reference uses the same extended angular domain
        // as the received field, NOT only the upper hemisphere. Otherwise
        // 48 incident directions are divided by 32 and outdoor gain clips
        // prematurely. An isolated gap contributes just its solid angle.
        internal static float AngularWeight(float directionY) =>
            directionY >= -.5f ? 1f : 0f;

        // An actual cached wall's source-to-receiver material spectrum:
        // use the same Occlusion.Wall and SpectralPaths coefficients as
        // sound effects. No new physics and no artificial weather bass bed.
        internal static void Transfer(Solids.Info material,
            out float gain, out float cutoff)
        {
            if (material == null)
            {
                gain = 0f;
                cutoff = 500f;
                return;
            }
            if (material.Ground)
            {
                // Ground immediately below a cellar is not automatically
                // a rain-emitting EXTERIOR surface. It is a boundary.
                gain = 0f;
                cutoff = 500f;
                return;
            }
            Occlusion.Block block = Occlusion.Block.Clear;
            if (material.Wall)
            {
                block.Walls = 1;
                block.Through = material.First;
                block.ThroughHigh = SpectralPaths.Response(material.Cut);
            }
            else
                block.Soft = material.Weight;
            Occlusion.Wall(block, out gain, out cutoff);
        }

        internal static float Normalize(float received, float reference) =>
            reference > 1e-6f ?
                Mathf.Clamp01(Mathf.Max(0f, received) / reference) : 0f;

        // Called ONCE after each existing Room.Measure. Each direction
        // has already consumed its 3 physics rays, cached material and
        // seam/portal classification. No extra RaycastCommands, heightmap
        // calls, audio sources, native arrays or frame hooks.
        internal static void Measure(Vector3[] directions,
            RaycastHit[] picked, bool[] hit)
        {
            float low = 0f, high = 0f, reference = 0f;
            float directEnergy = 0f, wallEnergy = 0f;
            int openings = 0, walls = 0;
            // Incoming energy and the fully open outdoor reference
            // must have identical integration weights.
            for (int i = 0; i < directions.Length; i++)
                reference += AngularWeight(directions[i].y);

            for (int i = 0; i < directions.Length; i++)
            {
                float w = AngularWeight(directions[i].y);
                if (w <= 0f) continue;
                bool open = Room.Out(i);
                float gain, cutoff;
                if (open)
                {
                    // A ray through a real arch, window or exterior region
                    // carries the field at its full direct transfer. Its
                    // actual angular share supplies the emitting AREA:
                    // a tiny hole never gets the level of a broad courtyard.
                    gain = 1f;
                    cutoff = 22000f;
                    openings++;
                }
                else
                {
                    // A one-ray seam is classified by the adjacent real
                    // boundary, not by distant terrain behind the gap.
                    int boundary = Room.AcousticBoundary(i);
                    Solids.Info material = boundary >= 0 &&
                        boundary < picked.Length ?
                        Solids.Of(picked[boundary]) : null;
                    Transfer(material, out gain, out cutoff);
                    walls++;
                }
                float energy = w * gain * gain;
                low += energy;
                high += w * SpectralPaths.HighEnergy(gain, cutoff);
                if (open) directEnergy += energy;
                else wallEnergy += energy;
            }

            // The same low/high energy fit used by normal WA multipath
            // SFX. Material and aperture contributions share one filter.
            SpectralPaths.Decode(Normalize(low, reference),
                Normalize(high, reference), out float outputGain,
                out float outputCutoff);
            Gain = outputGain;
            Cutoff = outputCutoff;
            Ready = true;
            s_direct = directEnergy / Mathf.Max(.001f, reference);
            s_boundary = wallEnergy / Mathf.Max(.001f, reference);
            s_high = Normalize(high, reference);
            s_openDirections = openings;
            s_wallDirections = walls;
            s_surveys++;
            s_domain = EmitterDomain.ExteriorSurface;
        }

        internal static string Summary() =>
            "environment field " + s_domain +
            ": open rays " + s_openDirections +
            ", blocked rays " + s_wallDirections +
            ", direct energy " + s_direct.ToString("F2") +
            ", transmitted energy " + s_boundary.ToString("F2") +
            ", high " + s_high.ToString("F2") +
            ", response " + Gain.ToString("F2") + "/" +
            Cutoff.ToString("F0") + " Hz" +
            ", field surveys " + s_surveys +
            ", EXTRA RAYS 0";

        internal static void Reset()
        {
            Ready = false;
            Gain = 1f;
            Cutoff = 22000f;
            s_openDirections = s_wallDirections = s_surveys = 0;
            s_direct = s_boundary = s_high = 0f;
        }
    }
}
