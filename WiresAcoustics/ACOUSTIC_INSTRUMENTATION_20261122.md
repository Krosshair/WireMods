# Wire's Acoustics 0.7.0 — audible-path diagnostics and job-invalidation hardening
Date: 2026-11-22
Status: LOCAL DEVELOPMENT BUILD (Enngard); NOT RELEASED. Never described as acoustically perfect or Steam Audio equivalent.

## Actual work completed

1. Main-thread acoustic snapshots (AcousticTrace.cs) run on the EXISTING three-second debug log report, and only if Debug log is enabled.
   - Top 24 approximately audible/active sources, actual name/clip/playing/virtual/mute/priority/pitch/spatialness, type and position, listener position.
   - Source-relative distance, latest propagation path class, indirect route count, route spread/point, last geometry generation, age of last propagation result, obstruction.
   - Filter's target occlusion gain/cutoff and weather air cutoff, source/physical distance gain, target composite DSP gain/cutoff, ACTUAL DSP gain/cutoff read from the callback's last state, stereo HRTF enabled, Doppler, immersion, reverb send.
   - Per-source samples, pre/post-filter RMS and dB ratio from existing Debug Meter path in AcousticFilter.OnAudioFilterRead. These values are real audio-buffer energy (before final Unity audio mixing), rather than guesses based only on geometry.
   - Fixed-point text formatting uses CultureInfo.InvariantCulture; numeric data is machine parseable regardless of Slovenian locale.

2. Event history
   - Bounded ring captures significant target gain and cutoff changes (1.5 dB, log-cutoff .30) with sound identity, timestamp, world generation and path class. Emitted at next debug report.
   - Separate bounded ring captures geometry modifications as global/local/door with coordinates and generation.
   - Trace reports overwritten entries if debug event volume exceeds the bounded ring. No gameplay or audio output recording, no new per-frame pollers, no additional audio per-sample loops, no unbounded allocations when debug off.

3. Geometry-in-flight safety
   - Batch now invalidates results if WORLD GEOMETRY changes on ANY previously sampled path: direct, both entrance and exit legs of each candidate opening, and both legs of each cached corner.
   - Discards/requeues affected results rather than silently applying an old wall state. Adds stale-job retry count to existing debug log.
   - Geometry change events also evaluate active indirect paths and potential portal paths, not only listener-to-source direct line, so a newly closed door on a side route will be rechecked.
   - Sound starts now tag their ray jobs with world generation, and requeue if geometry changes near the held source. Significant movement can cause one additional raycast pass, but is capped to prevent starvation of continuously moving sound sources. Reports retry counts.
   - All checks use EXISTING RayJobs and update callbacks; no Update/LateUpdate/FixedUpdate, coroutines or polling added.

4. Offline diagnostic parser
   - dev\AnalyzeAcousticTrace.ps1 accepts BepInEx LogOutput.log, produces ACOUSTIC_TRACE_REPORT.txt with path distribution, high-gain-change events, recent metadata, and possible anomalies.
   - Does not pretend to reproduce actual final sound waveform or spatial hearing. Signal measurements are per-source PRE-MIX output, not the final audio device output.

## Tests
- Built net472 project: 0 errors, 0 warnings.
- dev\AcousticTraceTests compiles actual AcousticTrace.cs with synthetic AudioSource/Unity state and confirms metadata, event history, geometry logging, invariant numeric format.
- dev\SharedEdgesTests validates actual cache code against deterministic stubs.
- dev\TestFresnelEdge.ps1 checks actual compiled Fresnel model; numeric reference comparisons.
- dev\TestSpectralPaths.ps1 checks frequency-band sum and cascade.
- dev\TestUnderwaterFraction.ps1 checks depth attenuation.
- dev\AnalyzeAcousticTrace.ps1 parsed actual harness output and generated report.
- No running game, live audio capture, frame timing profiling or Valheim mod-stack tests conducted. Therefore no guarantee of auditory equivalence, no zero-regression claim.

## What is STILL NOT finished
- Full engine-class path simulator (dynamic room/portal topology and all-multi-edge diffraction).
- Early reflections, frequency-resolved impulse-response reverb/convolution rather than Unity's parameterized reverb.
- Measured HRTF and arbitrary output speaker configurations.
- True full-band/material phase/impedance and diffraction/occlusion blending with coherent path arrivals.
- True final stereo output audio capture: cannot infer all Unity mixer, Windows spatial effects, audio hardware, and perceptual outcomes from per-source log measurements.

These require a real spatial-audio renderer/native integration or substantial DSP redesign. Numerical tests alone cannot certify a 'perfect' game audio mod.

## Enngard log and analysis
Enable Debug log in BepInEx configuration.
Log at C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\LogOutput.log
Run: powershell -ExecutionPolicy Bypass -File C:\Users\kajet\WireMods\WiresAcoustics\dev\AnalyzeAcousticTrace.ps1

## Rollback
Close Valheim; run backup\SOURCE_DIAGNOSTICS_20261122\Restore.ps1.
