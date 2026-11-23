using System;
using System.Text;
using System.Globalization;
using UnityEngine;

namespace WiresAcoustics
{
    // Only runs at the existing three-second room debug report, and only
    // with Debug Log enabled. Main-thread snapshot of actual AudioSource
    // state, geometry-result metadata and values sent to the DSP.
    // This is NOT an output waveform capture or a recording of what the
    // player hears: Unity mixing, audio output, HRTF and room reverb also
    // contribute and cannot be reconstructed from this metadata alone.
    internal static class AcousticTrace
    {
        private const int Top = 24;
        private static readonly AcousticFilter[] TopSources = new AcousticFilter[Top];
        private static readonly float[] TopScores = new float[Top];
        private static readonly StringBuilder Text = new StringBuilder(5200);
        private static int s_emitted;
        private const int History = 128;
        private struct Change
        {
            internal float At, OldGain, NewGain, OldCut, NewCut;
            internal string Name, Path;
            internal int Generation;
        }
        private static readonly Change[] Changes = new Change[History];
        private static int s_write, s_count, s_dropped;

        internal static void Record(AcousticFilter f, float oldGain, float oldCut,
                                    float newGain, float newCut)
        {
            if (!Plugin.DebugLog.Value) return;
            float dbDelta = Mathf.Abs(20f * Mathf.Log10(
                Mathf.Max(.00001f, newGain) / Mathf.Max(.00001f, oldGain)));
            float cutoffDelta = Mathf.Abs(Mathf.Log(
                Mathf.Max(1f, newCut) / Mathf.Max(1f, oldCut)));
            if (dbDelta < 1.5f && cutoffDelta < .30f) return;
            Changes[s_write] = new Change {
                At = Time.time, Name = f.gameObject.name,
                Path = f.AcousticPath, Generation = Occlusion.WorldGen,
                OldGain = oldGain, NewGain = newGain,
                OldCut = oldCut, NewCut = newCut
            };
            s_write = (s_write + 1) % History;
            if (s_count < History) s_count++;
            else s_dropped++;
        }

        private static void EmitTransitions()
        {
            if (s_count == 0) return;
            Plugin.Log.LogInfo("[acoustics.trace] significant transitions=" +
                s_count + ", overwritten=" + s_dropped +
                " (threshold: >=1.5 dB gain or >=0.30 log-cutoff)");
            int start = (s_write - s_count + History) % History;
            for (int i = 0; i < s_count; i++)
            {
                Change c = Changes[(start + i) % History];
                Text.Length = 0;
                Text.Append("[acoustics.trace.transition] t=").Append(c.At.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" source=").Append(c.Name)
                    .Append(" pathAtUpdate=").Append(c.Path)
                    .Append(" world=").Append(c.Generation)
                    .Append(" gain=").Append(c.OldGain.ToString("F4", CultureInfo.InvariantCulture))
                    .Append("->").Append(c.NewGain.ToString("F4", CultureInfo.InvariantCulture))
                    .Append(" cut=").Append(c.OldCut.ToString("F0", CultureInfo.InvariantCulture))
                    .Append("->").Append(c.NewCut.ToString("F0", CultureInfo.InvariantCulture)).Append("Hz");
                Plugin.Log.LogInfo(Text.ToString());
            }
            s_count = s_dropped = 0;
        }

        private const int EventHistory = 64;
        private struct WorldChange
        {
            internal float Time;
            internal Vector3 Position;
            internal int Generation;
            internal bool Global, Door;
        }
        private static readonly WorldChange[] WorldChanges = new WorldChange[EventHistory];
        private static int s_eventWrite, s_eventCount, s_eventDropped;

        internal static void RecordGeometry(Vector3 position, bool global, bool door, int generation)
        {
            if (Plugin.DebugLog == null || !Plugin.DebugLog.Value) return;
            WorldChanges[s_eventWrite] = new WorldChange {
                Time = Time.time, Position = position, Global = global,
                Door = door, Generation = generation
            };
            s_eventWrite = (s_eventWrite + 1) % EventHistory;
            if (s_eventCount < EventHistory) s_eventCount++;
            else s_eventDropped++;
        }

        private static void EmitGeometryChanges()
        {
            if (s_eventCount == 0) return;
            Plugin.Log.LogInfo("[acoustics.trace] geometryChanges=" +
                s_eventCount + " overwritten=" + s_eventDropped);
            int start = (s_eventWrite - s_eventCount + EventHistory) % EventHistory;
            for (int i = 0; i < s_eventCount; i++)
            {
                WorldChange c = WorldChanges[(start + i) % EventHistory];
                Text.Length = 0;
                Text.Append("[acoustics.trace.geometry] t=").Append(c.Time.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" generation=").Append(c.Generation)
                    .Append(" kind=").Append(c.Global ? "global" : c.Door ? "door" : "localized")
                    .Append(" at=").Append(c.Position.x.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                    .Append(c.Position.y.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                    .Append(c.Position.z.ToString("F2", CultureInfo.InvariantCulture));
                Plugin.Log.LogInfo(Text.ToString());
            }
            s_eventCount = s_eventDropped = 0;
        }

        internal static void Emit()
        {
            if (!Plugin.DebugLog.Value || !Plugin.Enabled.Value) return;
            for (int i = 0; i < Top; i++) { TopSources[i] = null; TopScores[i] = 0f; }
            int playing = 0, tracked = 0, routed = 0, older = 0;
            float maxAge = 0f;
            foreach (AcousticFilter f in AcousticFilter.Live)
            {
                if (f == null || f.Source == null || !f.Source.isPlaying) continue;
                playing++;
                if (f.Active) tracked++;
                if (f.Routed) routed++;
                float age = Mathf.Max(0f, Time.time - f.AcousticResolvedAt);
                if (f.Placed && age > 2f) older++;
                if (age > maxAge && f.Placed) maxAge = age;

                // Prioritize sources that could plausibly be heard, rather
                // than dumping every quiet ambience instance in a base.
                // Estimate, not Unity's final mixed output/voice selection.
                float score = Mathf.Max(0f, f.Source.volume) *
                    Mathf.Max(.00001f, Budget.Open(f) * f.TargetGain);
                if (!f.Active) score *= .05f;
                if (score <= 0f) continue;
                for (int i = 0; i < Top; i++)
                {
                    if (score <= TopScores[i]) continue;
                    for (int j = Top - 1; j > i; j--)
                    {
                        TopScores[j] = TopScores[j - 1];
                        TopSources[j] = TopSources[j - 1];
                    }
                    TopScores[i] = score;
                    TopSources[i] = f;
                    break;
                }
            }
            EmitGeometryChanges();
            EmitTransitions();
            Text.Length = 0;
            Text.Append("[acoustics.trace] snapshot=").Append(++s_emitted)
                .Append(" time=").Append(Time.time.ToString("F2", CultureInfo.InvariantCulture))
                .Append(" playing=").Append(playing)
                .Append(" active=").Append(tracked)
                .Append(" routed=").Append(routed)
                .Append(" pathOlderThan2s=").Append(older)
                .Append(" maxPathAge=").Append(maxAge.ToString("F2", CultureInfo.InvariantCulture))
                .Append("s selected=").Append(Top)
                .Append(" (estimated, not final rendered output)");
            Plugin.Log.LogInfo(Text.ToString());

            for (int i = 0; i < Top; i++)
            {
                AcousticFilter f = TopSources[i];
                if (f == null) continue;
                AudioSource src = f.Source;
                float age = Mathf.Max(0f, Time.time - f.AcousticResolvedAt);
                float targetGain = f.EstimatedCompositeGain;
                float targetCutoff = f.EstimatedCompositeCutoff;
                float db = 20f * Mathf.Log10(Mathf.Max(1e-6f, targetGain));
                Text.Length = 0;
                Text.Append("[acoustics.trace] #").Append(i + 1)
                    .Append(" name=").Append(f.gameObject.name)
                    .Append(" kind=").Append(f.Kind)
                    .Append(" clip=").Append(src.clip != null ? src.clip.name : "(stream/none)")
                    .Append(" volume=").Append(src.volume.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(" dist=").Append(f.LastDist.ToString("F2", CultureInfo.InvariantCulture))
                    .Append("m active=").Append(f.Active)
                    .Append(" spatial=").Append(src.spatialBlend.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" virtual=").Append(src.isVirtual)
                    .Append(" muted=").Append(src.mute)
                    .Append(" priority=").Append(src.priority)
                    .Append(" pitch=").Append(src.pitch.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(" path=").Append(f.AcousticPath)
                    .Append(" paths=").Append(f.AcousticRouteCount)
                    .Append(" routed=").Append(f.Routed)
                    .Append(" spread=").Append(f.RouteSpread.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" age=").Append(age.ToString("F2", CultureInfo.InvariantCulture)).Append("s")
                    .Append(" world=").Append(f.AcousticGeneration).Append('/').Append(Occlusion.WorldGen)
                    .Append(" occ=").Append(f.LastOcc.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" wallGain=").Append(f.TargetGain.ToString("F4", CultureInfo.InvariantCulture))
                    .Append(" wallLowpass=").Append(f.TargetCutoff.ToString("F0", CultureInfo.InvariantCulture)).Append("Hz")
                    .Append(" airLowpass=").Append(f.AirCutoff.ToString("F0", CultureInfo.InvariantCulture)).Append("Hz")
                    .Append(" distGain=").Append(f.DistanceGain.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(" sourceGain=").Append(f.SourceLevel.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(" targetDSP=").Append(targetGain.ToString("F4", CultureInfo.InvariantCulture))
                    .Append(" (").Append(db.ToString("F1", CultureInfo.InvariantCulture)).Append("dB)")
                    .Append(" targetDSPcut=").Append(targetCutoff.ToString("F0", CultureInfo.InvariantCulture)).Append("Hz")
                    .Append(" actualDSP=").Append(f.DspGain.ToString("F4", CultureInfo.InvariantCulture))
                    .Append(" actualDSPcut=").Append(f.DspCutoff.ToString("F0", CultureInfo.InvariantCulture)).Append("Hz")
                    .Append(" meterSamples=").Append(f.LastMeterSamples)
                    .Append(" meterDropped=").Append(f.LastDroppedMeterPackets)
                    .Append(" inputRMS=").Append(f.LastInputRms.ToString("F5", CultureInfo.InvariantCulture))
                    .Append(" outputRMS=").Append(f.LastOutputRms.ToString("F5", CultureInfo.InvariantCulture))
                    .Append(" processingChange=").Append(f.LastProcessingDb.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)).Append("dB")
                    .Append(" hrtf=").Append(f.Hrtf)
                    .Append(" doppler=").Append(f.Doppler.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(" water=").Append(f.UnderAmount.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" reverbSend=").Append(src.reverbZoneMix.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(" sourcePos=").Append(f.transform.position.x.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(',').Append(f.transform.position.y.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(',').Append(f.transform.position.z.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" listenerPos=").Append(Hearing.Eye.x.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(',').Append(Hearing.Eye.y.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(',').Append(Hearing.Eye.z.ToString("F2", CultureInfo.InvariantCulture));
                if (f.Routed)
                {
                    Vector3 d = f.RoutePoint - Hearing.Eye;
                    Text.Append(" routeDist=").Append(d.magnitude.ToString("F2", CultureInfo.InvariantCulture)).Append('m');
                }
                if (f.Records != null)
                {
                    int pending = 0, valid = 0;
                    foreach (Corners.Record rec in f.Records)
                    {
                        if (rec == null) continue;
                        if (rec.Valid) valid++;
                        pending += Mathf.Max(0, rec.Pending);
                    }
                    Text.Append(" edges=").Append(valid).Append(" pending=").Append(pending);
                }
                Plugin.Log.LogInfo(Text.ToString());
            }
        }
    }
}
