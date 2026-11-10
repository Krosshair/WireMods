using System.Collections.Generic;
using UnityEngine;

namespace WiresAcoustics
{
    // Per sound, on its own audio (Unity's filter callback, audio thread): occlusion (gain and a
    // one-pole low-pass) and our HRTF. Gain and low-pass glide (about 0.15 s) so nothing jumps or
    // clicks; only a sound that's just starting takes its values at once.
    //
    // The callback only runs while the component is enabled, and that's Budget's call: the sounds that
    // matter most (nearest, playing) are processed, the rest are left to Unity (its own panning, no
    // callback at all). Every callback is timed, so Budget can see how busy the audio thread is.
    //
    // Every change is smooth: values glide, the switch between the full and the cheaper HRTF
    // crossfades over one buffer, the echo fades out rather than stopping, and the callback is only
    // switched off once everything has glided back to untouched (a jump in the signal is a click).
    //
    // Air: the further a sound travels, the more of its highs the air swallows (a fight 60 m away is
    // duller, not just quieter). A low-pass from 22 kHz near you, about 15 kHz at 30 m, 9 kHz at
    // 60 m, 4.5 kHz at 100 m, never below 2.5 kHz (distance from Directions, every frame).
    //
    // Echo (Bounces): a delayed, duller copy of the sound from the direction of a big surface facing
    // you, panned by constant power. Its delay line comes from a pool when an echo is around.
    //
    // HRTF, modelled (no measured data): Unity's own panning is flattened (spread 180: both ears the
    // same), then per ear:
    //  - interaural time difference, Woodworth's formula: (a / c)(theta + sin theta), head radius
    //    8.75 cm, up to ~0.66 ms; a fractional delay on the far ear;
    //  - head shadow: the far ear loses its highs (low-pass 20 kHz down to 1.2 kHz fully to the side)
    //    and 6 dB overall, the near ear gains 1.6 dB (what a real head does to broadband sound;
    //    the highs differ by far more, which the low-pass gives);
    //  - behind you the outer ear shades the highs (above ~2.5 kHz) on both ears;
    //  - elevation: the outer ear's notch moving from ~5 kHz (below) to ~10 kHz (above), deeper the
    //    further a sound is above or below ear level (none within 12 degrees, -9 dB from 44 degrees),
    //    narrow (Q 4). A real ear's front response is offset by its own boosts; modelling only the
    //    dip made everything in front sound dull.
    // The direction comes from the main thread every frame (Room's AudioMan.Update hook).
    public sealed class AcousticFilter : MonoBehaviour
    {
        internal static readonly List<AcousticFilter> Live = new List<AcousticFilter>(256);
        private static volatile int s_rate = 48000;
        private static volatile float s_globalGain = 1f, s_globalCutoff = 22000f;
        internal static volatile bool GlobalActive;

        // Applied to every filtered sound on top of its own values (under water).
        // How strong the direction cues are (setting "HRTF strength": 1 as modelled, up to 2).
        internal static volatile float Strength = 1f;

        // Called by Budget's existing quarter-second main-thread pass.
        internal static void RefreshOutputFormat()
        {
            int rate = AudioSettings.outputSampleRate;
            if (rate > 0 && rate != s_rate) s_rate = rate;
        }

        internal static void SetGlobal(float gain, float cutoff, bool active)
        {
            s_globalGain = gain;
            s_globalCutoff = cutoff;
            GlobalActive = active;
        }

        // Audio-thread time spent in these callbacks (Stopwatch ticks), read and reset by Budget.
        // Split into the muffling and HRTF part and the echo part, with how many callbacks ran the HRTF.
        private static long s_ticks, s_ticksEcho;
        private static int s_calls, s_hrtfCalls;
        // Aggregated on the audio callback without formatting/logging and
        // only when diagnostics were requested on the main thread.
        private static long s_peakCallbackTicks;
        private static int s_qualityTransitions, s_crossfadeUnprepared;
        // Detailed tier counters are collected only while Debug log is on.
        internal static volatile bool Diagnostics;
        private static long s_ticksFull, s_ticksLight, s_ticksPlain;
        private static int s_callsFull, s_callsLight, s_callsPlain, s_callsSilent;
        internal static string DiagnosticsSummary()
        {
            long full = System.Threading.Interlocked.Exchange(ref s_ticksFull, 0L);
            long light = System.Threading.Interlocked.Exchange(ref s_ticksLight, 0L);
            long plain = System.Threading.Interlocked.Exchange(ref s_ticksPlain, 0L);
            int a = System.Threading.Interlocked.Exchange(ref s_callsFull, 0);
            int b = System.Threading.Interlocked.Exchange(ref s_callsLight, 0);
            int c = System.Threading.Interlocked.Exchange(ref s_callsPlain, 0);
            int silent = System.Threading.Interlocked.Exchange(ref s_callsSilent, 0);
            int transitions = System.Threading.Interlocked.Exchange(ref s_qualityTransitions, 0);
            int unprepared = System.Threading.Interlocked.Exchange(ref s_crossfadeUnprepared, 0);
            long peak = System.Threading.Interlocked.Exchange(ref s_peakCallbackTicks, 0L);
            double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            return "DSP full " + a + "/" + (full * ms).ToString("F1") + "ms, light " + b + "/" +
                (light * ms).ToString("F1") + "ms, plain " + c + "/" + (plain * ms).ToString("F1") +
                "ms, silent bypasses " + silent + ", quality crossfades " + transitions +
                ", unprepared-buffer switches " + unprepared +
                ", worst single callback " + (peak * ms).ToString("F3") + "ms";
        }
        internal static long TakeTicks(out int calls, out long echoTicks, out int hrtfCalls)
        {
            calls = System.Threading.Interlocked.Exchange(ref s_calls, 0);
            hrtfCalls = System.Threading.Interlocked.Exchange(ref s_hrtfCalls, 0);
            echoTicks = System.Threading.Interlocked.Exchange(ref s_ticksEcho, 0L);
            return System.Threading.Interlocked.Exchange(ref s_ticks, 0L);
        }

        private static volatile int s_channels, s_length;
        private volatile float m_targetGain = 1f, m_targetCutoff = 22000f;
        private volatile bool m_snap = true;
        private volatile bool m_hrtf;
        internal bool Hrtf
        {
            get => m_hrtf;
            set
            {
                if (m_hrtf == value) return;
                if (value) PrepareCrossfade(); // main thread, never audio callback
                m_hrtf = value;
                Directions.Want(this);
                Pan();
            }
        }
        private volatile float m_dx, m_dy, m_dz = 1f;
        internal float BaseSpread = -1f;
        internal AudioSource Source;
        internal bool Voice;       // a voice chat stream: muffled from Room's tick
        internal bool Routed;      // heard through an opening or round a corner: the HRTF places it there
        internal Vector3 RoutePoint;
        internal enum Kinds : byte { Sfx, Voice, Ocean, Other, Menu, Ambient }
        internal Kinds Kind;
        // Assigned at the actual vanilla producer's ZSFX.Play callsite.
        // Both sounds are parented to AudioMan and share the SAME prefab;
        // transform parent/name alone cannot distinguish spatial lava from
        // abstract biome ambience.
        internal bool WorldRandomAmbient, WorldLavaAmbient;
        // Vanilla ZSFX only refreshes looping reverb when this authored flag
        // is set. Unflagged loops need our existing batched geometry tick.
        internal bool NativeReverbLoop;
        internal float StartedAt, LastOcc, LastBlockDist;
        internal Collider LastBlocker; // prior confirmed obstruction, for bounded real-edge diffraction
        internal float PrevDist = -1f, RadialSpeed; // Doppler
        // Echo, per sound: how much later than the sound itself its echo arrives (its own path: from it
        // to the surface and back to you, against straight to you), and how strong (direct distance /
        // echo path: the echo spreads over the longer way). Directions, every frame.
        internal volatile float EchoDelay = .1f, EchoShare;
        internal volatile float Doppler = 1f;
        internal float LastDist = 1e6f;            // from the listener (Directions, Occlusion), for Budget
        internal bool InBudget;                    // in Budget's list of playing sounds
        // How wide the game made the source (its own spread, before ours): 0 a point, 1 all around.
        // Wide sources (a waterfall, a shield dome) get less of the HRTF's direction.
        internal volatile float Width;
        internal Transform ShapeRoot;              // what the sound comes from (Area): never hides it
        internal Vector3 ShapeCenter, ShapeHalf;   // its box: centre in its own space, half-size
        internal float AreaRadius;                 // 0: a point; otherwise how wide (Area)
        internal bool AreaKnown;
        internal float BaseWidth;                  // from the source's own spread; Width adds the area's size
        internal volatile bool Stereo = true;      // the clip has two channels (a mono one has no side to keep)
        internal Transform Player;                 // a player's own 2D sound, made placed (PlayerSounds)
        // Reversible conversion snapshots live only as long as this filter.
        internal bool ConvertedPlayer, ConvertedAmbient, OldPlayerBypass, SavedAmbientPan;
        internal float OldPlayerBlend, OldPlayerMin, OldPlayerMax, OldAmbientBlend, OriginalAmbientPan;
        internal Transform Facing;                 // the creature it comes out of (Follow): duller from behind it
        internal volatile float FacingCutoff = 22000f;
        internal bool Cue;                         // a game cue, not a sound in the world (a skill level-up): left unplaced
        internal volatile float DistanceGain = 1f;
        internal volatile float AirCutoff = 22000f;
        private bool m_placed;
        internal bool Placed                       // a placed sound: gets the air (Directions)
        {
            get => m_placed;
            set { if (m_placed != value) { m_placed = value; Directions.Want(this); } }
        }

        // Anything for the callback to do; if not, Budget leaves it switched off (no cost at all).
        internal bool NeedsWork =>
            m_hrtf || m_gain < .999f || m_cutoff < 20000f || m_echoLevel > 1e-4f || m_targetGain < .999f || m_targetCutoff < 20000f || AirCutoff < 20000f || FacingCutoff < 20000f ||
            DistanceGain < .999f || DistanceGain > 1.001f || GlobalActive || (s_echoGain > 1e-4f && LastDist <= 50f);

        // Always processed, whatever Budget's count: voices, the sea, the ambience loops, the menu.
        internal bool AlwaysOn => Voice || Kind == Kinds.Ocean || Kind == Kinds.Ambient || Kind == Kinds.Menu;
        // Used only by the existing quarter-second Budget sort (main thread).
        // A distant nearly-silent sound does not deserve more HRTF cycles
        // than a clearly audible creature nearby.
        internal float Audibility
        {
            get
            {
                if (Source == null) return 0f;
                float near = Mathf.Max(.5f, Source.minDistance);
                float distance = Mathf.Max(1f, LastDist / near);
                return Mathf.Max(0f, Source.volume) *
                    Mathf.Clamp01(m_targetGain) * Mathf.Clamp01(DistanceGain) *
                    Mathf.Clamp01(s_globalGain) / distance;
            }
        }

        internal static float AirAt(float dist) =>
            Mathf.Max(2500f, 22000f * Mathf.Exp(-Mathf.Max(0f, dist - 5f) / 60f));

        // Realistic distance: how far a sound carries depends on how loud it is. The game's own range
        // for it is what says that (a sconce's crackle is set to a few metres, a hearth to 10, a troll's
        // roar to 50): it fades out from 1.5 to 3 times that range, below what you'd hear over the
        // world around you. So quiet sounds go quiet sooner, loud ones carry.
        internal float FadeAt(float dist)
        {
            if (!m_physical || m_origMax <= 0f) return 1f;
            float t = Mathf.Clamp01((dist - 1.5f * m_origMax) / (1.5f * m_origMax));
            return 1f - t * t * (3f - 2f * t);
        }

        // Takes over the source's panning for the HRTF, remembering how wide it was.
        internal void TakePanning(float ownSpread)
        {
            if (Source == null) return;
            if (BaseSpread < 0f)
            {
                BaseSpread = ownSpread;
                // A source's spread is how the game's sound designers chose to mix it, not how big it
                // is (most are set 50 to 120 degrees, which took most of the direction away). Only a
                // sound made to come from all around (spread 150 to 180) is treated as wide; a
                // thing's real size comes from its shape (Area).
                BaseWidth = Mathf.Clamp01((ownSpread - 150f) / 30f);
                Width = BaseWidth;
            }
            Pan();
        }

        // Who pans the source: our HRTF while this filter runs (spread 180), Unity (its own spread)
        // while it doesn't.
        private void Pan()
        {
            if (Source == null || BaseSpread < 0f) return;
            float want = m_hrtf && enabled && m_active ? 180f : BaseSpread;
            if (Source.spread != want) Source.spread = want;
        }

        private volatile float m_gain = 1f, m_cutoff = 22000f;
        private readonly float[] m_state = new float[8];

        private const int Ring = 256;
        private const float HeadRadius = .0875f, Sound = 343f;
        private readonly float[] m_ring = new float[Ring];
        private int m_pos;
        private volatile bool m_primed;
        private float m_mono, m_side, m_delayL, m_delayR, m_headL, m_headR, m_backL, m_backR, m_gainL = 1f, m_gainR = 1f;
        private float m_x1L, m_x2L, m_y1L, m_y2L, m_x1R, m_x2R, m_y1R, m_y2R;

        // Directions' list: where this filter is in it, and whether it should be.
        internal int Slot = -1;
        internal bool Queued;
        private bool m_alive;
        private int m_liveIndex = -1;
        internal bool Tracked => m_alive && (m_hrtf || m_placed);

        // Registered for its whole life (Awake to OnDestroy), processing or not: enabling and
        // disabling is only about the audio callback.
        private void Awake()
        {
            s_rate = AudioSettings.outputSampleRate;
            Source = GetComponent<AudioSource>();
            // Added from a prefab (not serialized): a plain source without the game's sound-effect
            // component is one of the other placed sounds.
            if (GetComponent<ZSFX>() == null) Kind = Kinds.Other;
            // (No distance fade: Unity's logarithmic rolloff holds its level past the max distance, it
            // doesn't cut off. Measured in game: a sound 41 m out with a 10 m range still plays. So the
            // game's own range is left exactly as it is.)
            m_alive = true;
            m_liveIndex = Live.Count;
            Live.Add(this);
            Directions.Want(this);
        }

        private void OnDestroy()
        {
            m_alive = false;
            if (m_liveIndex >= 0 && m_liveIndex < Live.Count && ReferenceEquals(Live[m_liveIndex], this))
            {
                // swap-remove: sounds come and go many times a second
                int last = Live.Count - 1;
                AcousticFilter moved = Live[last];
                Live[m_liveIndex] = moved;
                moved.m_liveIndex = m_liveIndex;
                Live.RemoveAt(last);
            }
            else Live.Remove(this);
            m_liveIndex = -1;
            Directions.Want(this);
            ReleaseEcho();
        }

        // Processing on or off. The component itself stays enabled: switching a sound's filter on or
        // off makes Unity wait for its audio mixer to finish what it's mixing, up to a few ms each
        // time (measured, many times a second), so it's switched once and this flag says whether the
        // callback does anything. Off, a sound passes straight through.
        private volatile bool m_active = true;
        internal bool Active
        {
            get => m_active;
            set
            {
                if (m_active == value) return;
                m_active = value;
                if (value) m_primed = false;
                Pan();
            }
        }

        private void OnEnable()
        {
            m_primed = false;
            Pan();
        }

        private void OnDisable()
        {
            Pan();
            ReleaseEcho();
        }

        // Echo, shared by every sound (it's where you are that echoes): level, delay, pan, dullness.
        // Delay lines are pooled and never freed (only the processed sounds hold one).
        private const int EchoRing = 16384;
        private static readonly Stack<float[]> EchoPool = new Stack<float[]>();
        // Retire on MAIN thread. Recycle only after the audio callback that
        // may have captured the old array finishes. No locks/allocations on
        // the real-time audio thread; destroyed sources stay referenced until
        // the last callback exits and their ring can safely return to pool.
        private struct RetiredEcho
        {
            internal AcousticFilter Owner;
            internal float[] Samples;
        }
        private static readonly List<RetiredEcho> RetiredEchoes = new List<RetiredEcho>();
        private int m_echoReaders;
        private static volatile float s_echoGain, s_echoL = .707f, s_echoR = .707f, s_echoCut = 4000f;
        private float[] m_echo;
        private int m_echoPos;
        private volatile float m_echoLevel;
        private float m_echoLp, m_echoAt;

        // Main thread, every frame: direction relative to your head (x right, z forward).
        internal static void SetEcho(float gain, Vector3 local)
        {
            s_echoGain = gain;
            float x = Mathf.Clamp(local.x, -1f, 1f);
            s_echoL = Mathf.Sqrt((1f - x) * .5f);
            s_echoR = Mathf.Sqrt((1f + x) * .5f);
            s_echoCut = Mathf.Lerp(4000f, 2200f, Mathf.Clamp01(-local.z)); // from behind: the outer ear shades it
        }

        // Main thread: a delay line for this sound's echo while it's processed and an echo is around.
        internal void EnsureEcho()
        {
            // a far sound's echo is too faint to hear; a loop's echo is the loop again, just later (you
            // don't hear a steady sound echo, only its starts and stops)
            if (System.Threading.Volatile.Read(ref m_echo) != null || Cue || !enabled || !m_active ||
                LastDist > 50f || (Source != null && Source.loop) ||
                System.Threading.Volatile.Read(ref m_echoReaders) != 0) return;
            float[] e = EchoPool.Count > 0 ? EchoPool.Pop() : new float[EchoRing];
            System.Array.Clear(e, 0, e.Length); // main thread, once per sound
            m_echoPos = 0;
            m_echoLevel = 0f;
            m_echoLp = 0f;
            m_echoAt = 0f;
            System.Threading.Volatile.Write(ref m_echo, e);
        }

        private void ReleaseEcho()
        {
            float[] e = System.Threading.Interlocked.Exchange(ref m_echo, null);
            if (e == null) return;
            RetiredEchoes.Add(new RetiredEcho { Owner = this, Samples = e });
        }

        // No echo around: the delay lines go back to the pool (Budget, a few times a second).
        internal static void DropEchoes()
        {
            foreach (AcousticFilter f in Live)
                if (f != null && f.m_echo != null && f.m_echoLevel < 1e-4f &&
                    (s_echoGain < 1e-4f || !f.m_active)) f.ReleaseEcho();
            for (int i = RetiredEchoes.Count - 1; i >= 0; i--)
            {
                RetiredEcho retired = RetiredEchoes[i];
                if (System.Threading.Volatile.Read(ref retired.Owner.m_echoReaders) != 0)
                    continue;
                if (EchoPool.Count < 128) EchoPool.Push(retired.Samples);
                RetiredEchoes.RemoveAt(i);
            }
        }

        internal void Set(float gain, float cutoff, bool snap)
        {
            m_targetGain = gain;
            m_targetCutoff = cutoff;
            if (snap) m_snap = true;
        }

        internal void SetDirection(Vector3 d) { m_dx = d.x; m_dy = d.y; m_dz = d.z; }

        // HRTF switched off: every live sound gets Unity's own panning back.
        internal static void ReleaseHrtf()
        {
            foreach (AcousticFilter f in Live)
            {
                if (f == null) continue;
                f.Hrtf = false;
                if (f.BaseSpread >= 0f && f.Source != null) f.Source.spread = f.BaseSpread;
                f.BaseSpread = -1f;
            }
        }

        // The mod switched off: every live sound back to untouched, and no callbacks.
        internal static void ReleaseAll()
        {
            ReleaseHrtf();
            foreach (AcousticFilter f in Live)
            {
                if (f == null) continue;
                f.Set(1f, 22000f, false);
                f.DistanceGain = 1f;
                f.AirCutoff = 22000f;
                f.Placed = false;
                f.Physical(false);
                if (f.enabled) f.enabled = false; // the mod off: no callbacks at all
            }
            Budget.Clear();
        }

        // Debug-only read; target values alone can look correct even if the
        // source was bypassed by Budget or the DSP had not yet reached them.
        internal string CurrentProcessing() =>
            "processed=" + m_active + ", actual gain=" + m_gain.ToString("F2") +
            ", actual cutoff=" + m_cutoff.ToString("F0") + " Hz";

        // Debug: what this filter is doing to the sound right now.
        internal string Explain() =>
            "gain " + m_targetGain.ToString("F2") + ", cut " + m_targetCutoff.ToString("F0") + ", air " + AirCutoff.ToString("F0") + ", facing " + FacingCutoff.ToString("F0") +
            ", distance " + DistanceGain.ToString("F2") + (m_physical ? " (game's range " + m_origMax.ToString("F0") + " m)" : "") + ", width " + Width.ToString("F2") +
            (Source != null ? ", " + Source.rolloffMode + " " + Source.minDistance.ToString("F0") + "-" + Source.maxDistance.ToString("F0") + " m" : "");

        // Realistic distance (setting): the game's rolloff (linear and custom curves that reach silence
        // within a few metres, logarithmic ones that stop getting quieter at their range) replaced by
        // the inverse-distance law, 6 dB quieter each time the distance doubles, from the game's own
        // full-volume distance, out to 200 times that. The game's settings are kept and put back.
        private bool m_physical;
        private AudioRolloffMode m_origMode;
        private float m_origMin, m_origMax, m_origDoppler;
        internal void Physical(bool on)
        {
            if (Source == null || on == m_physical) return;
            if (on)
            {
                m_origMode = Source.rolloffMode;
                m_origMin = Source.minDistance;
                m_origMax = Source.maxDistance;
                m_origDoppler = Source.dopplerLevel;
                Source.dopplerLevel = 0f; // ours instead (Doppler)
                Source.rolloffMode = AudioRolloffMode.Logarithmic;
                Source.minDistance = Mathf.Max(.5f, m_origMin);
                Source.maxDistance = Mathf.Max(.5f, m_origMin) * 200f;
            }
            else
            {
                Source.rolloffMode = m_origMode;
                Source.minDistance = m_origMin;
                Source.maxDistance = m_origMax;
                Source.dopplerLevel = m_origDoppler;
                Doppler = 1f;
            }
            m_physical = on;
        }

        internal static string Format()
        {
            string r = s_channels > 0 ? "filter sees " + s_channels + " channel(s), " + s_length + " samples per buffer at " + s_rate + " Hz" : "filter not run yet";
            return r + ", " + Live.Count + " sounds known";
        }

        // Debug (Budget, with Debug log on): this sound's level going in and coming out, to see
        // what the processing does to loudness. Sums of squares since the last report.
        internal static volatile bool Meter;
        internal double MeterIn, MeterOut;

        // Budget: the nearest processed sounds get the full HRTF; the rest a cheaper version (the
        // level difference between the ears only: no delay, no ear filters).
        internal volatile bool Full = true;

        private static double Energy(float[] d)
        {
            double e = 0d;
            for (int i = 0; i < d.Length; i++) e += d[i] * d[i];
            return e;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool meter = Meter, diagnostic = Diagnostics;
            if (meter) MeterIn += Energy(data);
            // A source already below -80 dB and still targeting silence
            // cannot be heard; zero its existing buffer, rather than running
            // ~2,000 samples of HRTF/low-pass code. The echo can still have
            // audible delayed samples, so it MUST be empty/inactive first.
            float target = m_targetGain * s_globalGain * DistanceGain;
            bool echoSilent = m_echo == null ||
                (m_echoLevel < 1e-5f && s_echoGain * EchoShare < 1e-5f);
            bool silent = m_gain < 1e-4f && target < 1e-4f && echoSilent;
            long t1;
            if (silent)
            {
                System.Array.Clear(data, 0, data.Length);
                m_gain = target;
                // Avoid stale low-pass feedback when a silent sound resumes.
                System.Array.Clear(m_state, 0, m_state.Length);
                m_primed = false;
                m_wasFullKnown = false;
                t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            else
            {
                Process(data, channels);
                t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                Echo(data, channels);
            }
            if (meter) MeterOut += Energy(data);
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (diagnostic)
            {
                long duration = t2 - t0;
                long prior = System.Threading.Interlocked.Read(ref s_peakCallbackTicks);
                while (duration > prior)
                {
                    long observed = System.Threading.Interlocked.CompareExchange(
                        ref s_peakCallbackTicks, duration, prior);
                    if (observed == prior) break;
                    prior = observed;
                }
            }
            System.Threading.Interlocked.Add(ref s_ticks, t2 - t0);
            System.Threading.Interlocked.Add(ref s_ticksEcho, t2 - t1);
            System.Threading.Interlocked.Increment(ref s_calls);
            bool hrtf = m_hrtf && channels >= 2;
            if (hrtf && !silent) System.Threading.Interlocked.Increment(ref s_hrtfCalls);
            if (diagnostic)
            {
                if (silent) System.Threading.Interlocked.Increment(ref s_callsSilent);
                else if (hrtf && Full)
                {
                    System.Threading.Interlocked.Add(ref s_ticksFull, t1 - t0);
                    System.Threading.Interlocked.Increment(ref s_callsFull);
                }
                else if (hrtf)
                {
                    System.Threading.Interlocked.Add(ref s_ticksLight, t1 - t0);
                    System.Threading.Interlocked.Increment(ref s_callsLight);
                }
                else
                {
                    System.Threading.Interlocked.Add(ref s_ticksPlain, t1 - t0);
                    System.Threading.Interlocked.Increment(ref s_callsPlain);
                }
            }
        }

        private void Echo(float[] data, int ch)
        {
            // Almost all sources have no echo allocated. This first check
            // only tests null: it does not capture a usable buffer pointer.
            // The protected second read below is the ONLY pointer we use.
            if (System.Threading.Volatile.Read(ref m_echo) == null) return;
            // Publish reader entry BEFORE acquiring the buffer reference.
            // Main-thread ReleaseEcho atomically detaches its pointer and
            // retires it until no callback could still reference it.
            System.Threading.Interlocked.Increment(ref m_echoReaders);
            try
            {
            float[] e = System.Threading.Volatile.Read(ref m_echo);
            float g = Full ? s_echoGain * EchoShare : 0f; // the nearest sounds only (it was over half the cost); fades out, not cut
            if (e == null || ch < 1) return;
            float g0 = m_echoLevel;
            if (g < 1e-4f && g0 < 1e-4f) return;
            int frames = data.Length / ch;
            if (frames == 0) return;
            int mask = e.Length - 1;
            float target = Mathf.Clamp(EchoDelay * s_rate, 1f, e.Length - 2);
            float d0 = m_echoAt > 0f ? m_echoAt : target;
            float d1 = Mathf.MoveTowards(d0, target, frames * .02f); // slow, so a moving echo doesn't warble
            m_echoAt = d1;
            m_echoLevel = g;
            float gl = s_echoL, gr = s_echoR;
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * s_echoCut / s_rate);
            int pos = m_echoPos;
            float lp = m_echoLp, inv = 1f / frames, dd = d1 - d0, dg = g - g0;
            for (int i = 0, k = 0; i < frames; i++, k += ch)
            {
                float t = i * inv;
                e[pos] = ch >= 2 ? (data[k] + data[k + 1]) * .5f : data[k];
                // whole-sample delay: an echo 30 m off doesn't need sub-sample precision
                float y = e[(pos - (int)(d0 + dd * t)) & mask];
                pos = (pos + 1) & mask;
                lp += a * (y - lp);
                float v = lp * (g0 + dg * t);
                if (ch >= 2) { data[k] += v * gl; data[k + 1] += v * gr; }
                else data[k] += v;
            }
            m_echoPos = pos;
            m_echoLp = lp;
            }
            finally { System.Threading.Interlocked.Decrement(ref m_echoReaders); }
        }

        private void Process(float[] data, int channels)
        {
            s_channels = channels;
            s_length = data.Length;
            float tg = m_targetGain * s_globalGain * DistanceGain;
            float tc = m_targetCutoff;
            if (s_globalCutoff < tc) tc = s_globalCutoff;
            if (AirCutoff < tc) tc = AirCutoff;
            if (FacingCutoff < tc) tc = FacingCutoff;
            if (m_snap)
            {
                m_snap = false;
                m_gain = tg; m_cutoff = tc;
                for (int c = 0; c < m_state.Length; c++) m_state[c] = 0f;
                m_primed = false;
            }
            if (channels <= 0 || channels > m_state.Length) return;
            if (m_hrtf && channels >= 2)
            {
                bool full = Full;
                if (full != m_wasFull && m_wasFullKnown)
                    Crossfade(data, channels, tg, tc, full); // switching level: no jump
                else if (full) Spatialize(data, channels, tg, tc);
                else Pan2(data, channels, tg, tc);
                m_wasFull = full;
                m_wasFullKnown = true;
                return;
            }
            m_primed = false;
            if (m_gain >= .999f && tg >= .999f && m_cutoff >= 20000f && tc >= 20000f)
            {
                // Untouched; the filter state follows the signal so closing it later doesn't click.
                for (int c = 0; c < channels && data.Length >= channels; c++) m_state[c] = data[data.Length - channels + c];
                return;
            }
            int frames = data.Length / channels;
            float glide = Glide(frames);
            float g0 = m_gain, g1 = Mathf.Abs(tg - m_gain) < 1e-4f ? tg : m_gain + (tg - m_gain) * glide;
            m_cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(m_cutoff), Mathf.Log(tc), glide));
            m_gain = g1;
            float a = m_cutoff >= 20000f ? 1f : 1f - Mathf.Exp(-2f * Mathf.PI * m_cutoff / s_rate);
            float[] st = m_state;
            float inv = 1f / frames, dg = g1 - g0;
            for (int i = 0, k = 0; i < frames; i++)
            {
                float g = g0 + dg * i * inv;
                for (int c = 0; c < channels; c++, k++)
                {
                    float y = st[c] += a * (data[k] - st[c]);
                    data[k] = y * g;
                }
            }
        }

        private void Spatialize(float[] data, int ch, float tg, float tc)
        {
            int frames = data.Length / ch;
            if (frames == 0) return;

            // occlusion
            float glide = Glide(frames);
            float g0 = m_gain, g1 = Mathf.Abs(tg - m_gain) < 1e-4f ? tg : m_gain + (tg - m_gain) * glide;
            m_cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(m_cutoff), Mathf.Log(tc), glide));
            m_gain = g1;
            float aOcc = m_cutoff >= 20000f ? 1f : 1f - Mathf.Exp(-2f * Mathf.PI * m_cutoff / s_rate);

            // direction
            float x = Mathf.Clamp(m_dx, -1f, 1f), y = Mathf.Clamp(m_dy, -1f, 1f), z = Mathf.Clamp(m_dz, -1f, 1f);
            float kw = (1f - Width) * Strength; // a wide source gets less direction
            float lat = Mathf.Abs(x), th = Mathf.Asin(lat);
            float itd = HeadRadius / Sound * (th + Mathf.Sin(th)) * s_rate * kw; // samples
            float tL = x > 0f ? itd : 0f, tR = x < 0f ? itd : 0f;          // the far ear hears it later
            float farA = Mathf.Lerp(1f, 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Exp(Mathf.Lerp(Mathf.Log(20000f), Mathf.Log(1200f), Mathf.Min(1f, lat * Mathf.Max(1f, kw)))) / s_rate), Mathf.Min(1f, kw));
            float aL = x > 0f ? farA : 1f, aR = x < 0f ? farA : 1f;
            float near = 1f + .2f * lat * kw, far = Mathf.Max(.1f, 1f - .5f * lat * kw);
            float tgL = x > 0f ? far : near, tgR = x < 0f ? far : near;
            float shade = Mathf.Min(.9f, .65f * Mathf.Clamp01(-z) * kw);
            bool back = shade > 1e-3f, stereo = Stereo;
            float aBack = 1f - Mathf.Exp(-2f * Mathf.PI * 2500f / s_rate);
            // RBJ peaking EQ for the outer-ear notch: narrow (Q 4, like a real pinna notch) and only
            // well above or below (a wide one dulled everything just below the camera)
            float f0 = Mathf.Clamp(7500f + 2500f * y, 3000f, s_rate * .45f);
            float notch = 9f * kw * Mathf.Clamp01((Mathf.Abs(y) - .2f) / .5f); // dB
            float A = Mathf.Pow(10f, -notch / 40f), w = 2f * Mathf.PI * f0 / s_rate, cw = Mathf.Cos(w), al = Mathf.Sin(w) / (2f * 4f);
            bool eq = notch > .05f; // level with you: no notch, no filter
            float a0 = 1f + al / A;
            float b0 = (1f + al * A) / a0, b1 = -2f * cw / a0, b2 = (1f - al * A) / a0, a1 = -2f * cw / a0, a2 = (1f - al / A) / a0;

            if (!m_primed)
            {
                // Starting (a new sound, a pooled one played again, or back from the light level): the
                // delay line and the filters start from this buffer's first sample, not from whatever
                // they held when they last ran (old audio read out for the first half millisecond:
                // a tick at the start).
                m_primed = true;
                m_delayL = tL; m_delayR = tR; m_gainL = tgL; m_gainR = tgR;
                float first = 0f;
                for (int c = 0; c < ch; c++) first += data[c];
                first /= ch;
                float v0 = first * g0;
                for (int r = 0; r < Ring; r++) m_ring[r] = v0;
                m_mono = first;
                m_side = stereo ? (data[0] - data[1]) * .5f : 0f;
                m_headL = m_headR = m_backL = m_backR = v0;
                m_x1L = m_x2L = m_y1L = m_y2L = m_x1R = m_x2R = m_y1R = m_y2R = v0;
            }
            float dL0 = m_delayL, dR0 = m_delayR, gL0 = m_gainL, gR0 = m_gainR;
            m_delayL = tL; m_delayR = tR; m_gainL = tgL; m_gainR = tgR;

            // the loop works on locals (the audio thread's hot path: no field access, no calls)
            float[] ring = m_ring;
            int pos = m_pos;
            float mono = m_mono, sideS = m_side, headL = m_headL, headR = m_headR, backL = m_backL, backR = m_backR;
            float x1L = m_x1L, x2L = m_x2L, y1L = m_y1L, y2L = m_y2L, x1R = m_x1R, x2R = m_x2R, y1R = m_y1R, y2R = m_y2R;
            float invCh = 1f / ch, invF = 1f / frames;
            float dg = g1 - g0, ddL = tL - dL0, ddR = tR - dR0, dgL = tgL - gL0, dgR = tgR - gR0;
            const int mask = Ring - 1;
            for (int i = 0; i < frames; i++)
            {
                float t = i * invF;
                int k = i * ch;
                float m = 0f;
                for (int c = 0; c < ch; c++) m += data[k + c];
                m *= invCh;
                float g = g0 + dg * t;
                // Stereo clips keep their width: the difference between their channels rides on top,
                // muffled like the rest (mono clips have none).
                float sw = 0f;
                if (stereo)
                {
                    float side = (data[k] - data[k + 1]) * .5f;
                    sideS += aOcc * (side - sideS);
                    sw = sideS * g;
                }
                mono += aOcc * (m - mono);
                ring[pos] = mono * g;

                float p = pos - (dL0 + ddL * t);
                int i0 = (int)p; if (i0 > p) i0--;
                float fr = p - i0;
                float vL = ring[i0 & mask] * (1f - fr) + ring[(i0 + 1) & mask] * fr;
                p = pos - (dR0 + ddR * t);
                i0 = (int)p; if (i0 > p) i0--;
                fr = p - i0;
                float vR = ring[i0 & mask] * (1f - fr) + ring[(i0 + 1) & mask] * fr;
                pos = (pos + 1) & mask;

                headL += aL * (vL - headL); vL = headL;
                headR += aR * (vR - headR); vR = headR;
                if (back)
                {
                    backL += aBack * (vL - backL); vL -= shade * (vL - backL);
                    backR += aBack * (vR - backR); vR -= shade * (vR - backR);
                }
                float oL = vL, oR = vR;
                if (eq)
                {
                    oL = b0 * vL + b1 * x1L + b2 * x2L - a1 * y1L - a2 * y2L;
                    oR = b0 * vR + b1 * x1R + b2 * x2R - a1 * y1R - a2 * y2R;
                }
                x2L = x1L; x1L = vL; y2L = y1L; y1L = oL;
                x2R = x1R; x1R = vR; y2R = y1R; y1R = oR;
                data[k] = oL * (gL0 + dgL * t) + sw;
                data[k + 1] = oR * (gR0 + dgR * t) - sw;
                for (int c = 2; c < ch; c++) data[k + c] = 0f;
            }
            m_pos = pos;
            m_mono = mono; m_side = sideS; m_headL = headL; m_headR = headR; m_backL = backL; m_backR = backR;
            m_x1L = x1L; m_x2L = x2L; m_y1L = y1L; m_y2L = y2L; m_x1R = x1R; m_x2R = x2R; m_y1R = y1R; m_y2R = y2R;
        }

        // Keep two independent DSP histories for the duration of a quality
        // transition: the OLD algorithm must not advance the history from
        // which the NEW algorithm starts. These buffers allocate on the main
        // thread when HRTF is first enabled, never inside the audio callback.
        private float[] m_crossRing, m_crossState;
        private struct DspSnapshot
        {
            internal float Gain, Cutoff, Mono, Side, DelayL, DelayR;
            internal float HeadL, HeadR, BackL, BackR, GainL, GainR;
            internal float X1L, X2L, Y1L, Y2L, X1R, X2R, Y1R, Y2R;
            internal int Position;
            internal bool Primed;
        }
        private DspSnapshot SnapshotDsp()
        {
            System.Array.Copy(m_ring, m_crossRing, Ring);
            System.Array.Copy(m_state, m_crossState, m_state.Length);
            return new DspSnapshot
            {
                Gain = m_gain, Cutoff = m_cutoff,
                Mono = m_mono, Side = m_side,
                DelayL = m_delayL, DelayR = m_delayR,
                HeadL = m_headL, HeadR = m_headR,
                BackL = m_backL, BackR = m_backR,
                GainL = m_gainL, GainR = m_gainR,
                X1L = m_x1L, X2L = m_x2L, Y1L = m_y1L, Y2L = m_y2L,
                X1R = m_x1R, X2R = m_x2R, Y1R = m_y1R, Y2R = m_y2R,
                Position = m_pos, Primed = m_primed
            };
        }
        private void RestoreDsp(DspSnapshot s)
        {
            System.Array.Copy(m_crossRing, m_ring, Ring);
            System.Array.Copy(m_crossState, m_state, m_state.Length);
            m_gain = s.Gain; m_cutoff = s.Cutoff;
            m_mono = s.Mono; m_side = s.Side;
            m_delayL = s.DelayL; m_delayR = s.DelayR;
            m_headL = s.HeadL; m_headR = s.HeadR;
            m_backL = s.BackL; m_backR = s.BackR;
            m_gainL = s.GainL; m_gainR = s.GainR;
            m_x1L = s.X1L; m_x2L = s.X2L; m_y1L = s.Y1L; m_y2L = s.Y2L;
            m_x1R = s.X1R; m_x2R = s.X2R; m_y1R = s.Y1R; m_y2R = s.Y2R;
            m_pos = s.Position; m_primed = s.Primed;
        }

        // One buffer through both the old and the new HRTF level, faded from one to the other.
        private bool m_wasFull, m_wasFullKnown;
        private volatile float[] m_cross;
        private volatile int m_crossNeeded;
        // Called by the existing main-thread HRTF setter and Budget tick.
        // An unexpected device buffer-size increase is handled without
        // allocating in OnAudioFilterRead; the next Budget tick grows it.
        internal void PrepareCrossfade()
        {
            if (m_crossRing == null) m_crossRing = new float[Ring];
            if (m_crossState == null) m_crossState = new float[m_state.Length];
            int needed = m_crossNeeded;
            if (m_cross != null && m_cross.Length >= needed) return;
            if (needed <= 0)
            {
                AudioSettings.GetDSPBufferSize(out int samples, out int buffers);
                needed = Mathf.Max(2048, samples * 2);
            }
            m_cross = new float[Mathf.Max(needed, 2048)];
        }
        private void Crossfade(float[] data, int ch, float tg, float tc, bool toFull)
        {
            float[] cross = m_cross;
            if (cross == null || cross.Length < data.Length ||
                m_crossRing == null || m_crossState == null)
            {
                if (Diagnostics)
                    System.Threading.Interlocked.Increment(ref s_crossfadeUnprepared);
                // Audio callbacks cannot allocate or resize a buffer. Use
                // the requested new quality until main thread prepares a
                // larger scratch; this avoids an RT allocation or underrun.
                if (data.Length > m_crossNeeded) m_crossNeeded = data.Length;
                if (toFull) Spatialize(data, ch, tg, tc);
                else Pan2(data, ch, tg, tc);
                return;
            }
            if (Diagnostics)
                System.Threading.Interlocked.Increment(ref s_qualityTransitions);
            System.Array.Copy(data, cross, data.Length);
            DspSnapshot before = SnapshotDsp();
            if (toFull) Pan2(cross, ch, tg, tc); else Spatialize(cross, ch, tg, tc); // the old
            RestoreDsp(before); // both variants begin at the SAME sample history
            if (toFull) Spatialize(data, ch, tg, tc); else Pan2(data, ch, tg, tc);       // the new
            int frames = data.Length / ch;
            // End on the exact new output sample, not one sample below a
            // complete fade. The following buffer then continues the SAME
            // new DSP history, avoiding a one-sample quality-transition step.
            float inv = 1f / Mathf.Max(1, frames - 1);
            for (int i = 0, k = 0; i < frames; i++)
            {
                float t = frames > 1 ? i * inv : 1f;
                for (int c = 0; c < ch; c++, k++) data[k] = cross[k] + (data[k] - cross[k]) * t;
            }
        }

        // The cheaper HRTF for sounds past the nearest few: occlusion as usual, and only the level
        // difference between the ears (same amounts as the full one), on the stereo signal.
        private void Pan2(float[] data, int ch, float tg, float tc)
        {
            int frames = data.Length / ch;
            if (frames == 0) return;
            m_primed = false;
            float glide = Glide(frames);
            float g0 = m_gain, g1 = Mathf.Abs(tg - m_gain) < 1e-4f ? tg : m_gain + (tg - m_gain) * glide;
            m_cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(m_cutoff), Mathf.Log(tc), glide));
            m_gain = g1;
            float a = m_cutoff >= 20000f ? 1f : 1f - Mathf.Exp(-2f * Mathf.PI * m_cutoff / s_rate);
            float x = Mathf.Clamp(m_dx, -1f, 1f), kw = (1f - Width) * Strength, lat = Mathf.Abs(x);
            float near = 1f + .2f * lat * kw, far = Mathf.Max(.1f, 1f - .5f * lat * kw);
            float gL = x > 0f ? far : near, gR = x < 0f ? far : near;
            float gL0 = m_gainL, gR0 = m_gainR;
            m_gainL = gL; m_gainR = gR;
            float sL = m_state[0], sR = m_state[1], inv = 1f / frames, dg = g1 - g0, dL = gL - gL0, dR = gR - gR0;
            for (int i = 0, k = 0; i < frames; i++, k += ch)
            {
                float t = i * inv, g = g0 + dg * t;
                sL += a * (data[k] - sL);
                sR += a * (data[k + 1] - sR);
                data[k] = sL * g * (gL0 + dL * t);
                data[k + 1] = sR * g * (gR0 + dR * t);
                for (int c = 2; c < ch; c++) data[k + c] = 0f;
            }
            m_state[0] = sL; m_state[1] = sR;
        }

        // How far toward the target one buffer moves: a 0.15 s time constant whatever the buffer size.
        private static float Glide(int frames) => 1f - Mathf.Exp(-frames / (.15f * s_rate));
    }
}
