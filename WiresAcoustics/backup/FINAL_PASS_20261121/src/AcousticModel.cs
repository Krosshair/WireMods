using UnityEngine;

namespace WiresAcoustics
{
    // Shared physical description and rendering policy. A source's lifetime
    // and world geometry are independent of the listener's acoustic samples.
    // Runs on the main thread only; no DSP callback or per-object Update.
    internal static class AcousticModel
    {
        internal enum Shape : byte { Point, Aperture, Line, Diffuse }

        internal readonly struct Profile
        {
            internal readonly Shape Geometry;
            internal readonly float MinDistance, MaxDistance;
            internal readonly float ReverbFraction;
            internal readonly float ReferenceRadius;

            internal Profile(Shape geometry, float min, float max,
                float reverb, float referenceRadius = 1f)
            {
                Geometry = geometry;
                MinDistance = min;
                MaxDistance = max;
                ReverbFraction = Mathf.Clamp01(reverb);
                ReferenceRadius = Mathf.Max(.1f, referenceRadius);
            }
        }

        internal static readonly Profile OrdinarySound =
            new Profile(Shape.Point, 1f, 200f, 1f);
        // The rain through a window is primarily the outdoor direct field.
        // Room reflections enter weakly, rather than being sent to the room
        // reverb at the same level as a sound physically inside the room.
        internal static readonly Profile WindowRain =
            new Profile(Shape.Aperture, 2f, 40f, 0f, 2f);
        internal static readonly Profile WindowWind =
            new Profile(Shape.Aperture, 2f, 40f, 0f, 2f);
        internal static readonly Profile Shoreline =
            new Profile(Shape.Line, 2f, 200f, 0f);

        // Equal-energy environmental waveforms have amplitude proportional
        // to sqrt(area). For a roughly circular opening, area ~ radius^2
        // and therefore amplitude ~ radius. Independent windows add energy;
        // we deliberately do not impose an arbitrary global volume cap.
        internal static float ApertureAmplitude(float radius, Profile p)
        {
            return Mathf.Clamp(radius / p.ReferenceRadius, .25f, 2.5f);
        }

        internal static void Configure(AudioSource source, Profile p)
        {
            if (source == null) return;
            if (p.Geometry == Shape.Aperture)
            {
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = p.MinDistance;
                source.maxDistance = p.MaxDistance;
                source.dopplerLevel = 0f;
            }
            // A wetness policy must be explicit on the source itself.
            // Unity's room-wide AudioReverbZone otherwise treats an outside
            // source as though it were located in the listener's room.
            source.reverbZoneMix = p.ReverbFraction;
            source.bypassReverbZones = p.ReverbFraction <= .0001f;
        }

        // All placed environmental sources use the same final rendering
        // contract. Geometry-specific code only supplies path transmission,
        // spectrum and apparent angular size. This is shared by windows and
        // shorelines rather than applying independent DSP setup in both.
        internal static void Render(AcousticFilter filter, AudioSource source,
            Profile profile, float distance, float gain, float cutoff,
            float apparentWidth)
        {
            if (filter == null || source == null) return;
            filter.Placed = true;
            bool line = profile.Geometry == Shape.Line;
            filter.Physical(line && Plugin.RealDistance.Value, line);
            bool hrtf = Plugin.Hrtf.Value;
            if (hrtf)
            {
                filter.TakePanning(source.spread);
                filter.Width = apparentWidth;
            }
            else if (filter.BaseSpread >= 0f)
            {
                source.spread = filter.BaseSpread;
                filter.BaseSpread = -1f;
            }
            filter.Hrtf = hrtf;
            filter.Routed = false;
            filter.Set(gain, cutoff, false);
            Budget.Note(filter, distance);
        }

        // Shared distance/reverb-send policy for ordinary world sounds.
        // Called from existing Occlusion paths, not from the audio thread.
        internal static float RoomSend(float distance, float criticalDistance,
            float transmittedGain, Profile p)
        {
            float directVsDiffuse = distance /
                Mathf.Max(.01f, distance + Mathf.Max(.01f, criticalDistance));
            return Mathf.Clamp01(directVsDiffuse * transmittedGain *
                p.ReverbFraction);
        }
    }
}
