# Wire's Acoustics — lean ray-traced audio iteration
Date: 2026-11-21
Development version: 0.6.0 (unreleased)
Status: Release compiled (0 errors / 0 warnings), verified by 24 source checks, installed in both package and Gale Enngard. Listening/performance A-B test pending.
Installed DLL SHA256: 154FD0516AD7B244314A764350241B1C3178E83BD156D60F16EFF88AFC771D42
Source: C:\Users\kajet\WireMods\WiresAcoustics\src
Test profile: Gale / Enngard
Rollback: backup/LEAN_AUDIO_20261121/Restore.cmd

## Why this was changed

The previous architecture had separate physical window source registration,
9x9 / bounding-collider aperture reconstruction, cloned rain/wind loops,
four synthesized ocean shoreline regions and secondary aperture propagation
paths for ordinary sounds. The profiler recorded main-thread room-tick
averages of roughly 0.51–0.65 ms on the previous build; aperture detection
mostly rejected candidate windows and occasionally saturated its collider
buffer (128 results). This showed additional processing cost without a
dependable audible benefit.

## Retained features

- Direct, material-dependent ray-traced occlusion for real AudioSources,
  including walls/doors, terrain and obstacles.
- Limited verified secondary paths around nearby wall edges. Direct and
  secondary arrivals still combine with energy and precedence localization.
- Ray-informed room enclosure, volume, material absorption and reverb, with
  the existing stable room-value interpolation and Unity reverb zone.
- World-space source positions and physical-distance mode for ordinary SFX.
- Headphone HRTF, Doppler, air/wind absorption, travel-time delay,
  underwater attenuation, spatial placement and voice-chat compatibility.
- Random independent loop playback phases on fires/machines/sconces.
- Nonblocking Unity raycast and transform jobs; no new Update components.

## Removed and simplified

- All additional rain/window/wind AudioSources: only Valheim's original
  weather recordings remain, with gain/cutoff continuously interpolated
  using existing room-enclosure observations.
- Removed opening source registry and aperture geometry reconstruction.
  Room rays only classify seams/open sky for reverb and shelter.
- Removed extra aperture paths in Batch. The normal material obstruction
  path and cached corner diffraction remain.
- Removed 96-ray ocean scans and the multiple synthesized shoreline loops.
  The original ocean source is only filtered by shelter/exposure measured
  by existing room rays every 0.25 seconds.
- Reduced edge-search reach from 5.5 to 3.2 metres while keeping 0.2 m
  spatial steps; reduced the secondary probe budget from 1024 to 256.
- Removed unused environmental source profiles and renderer. AcousticModel
  retains the common direct/secondary propagation combination and room-send
  formula.

## Risks and explicit tradeoffs

- Rain now forms a continuous diffuse environmental field. It no longer
  points toward particular windows; this loses precise aperture direction
  cues in exchange for stable realistic ambience without source jumps.
- The ocean relies on Valheim's original placement, not custom shoreline
  geometry. Some additional directional detail has been removed.
- Shorter edge searches may weaken sound going around long, distant
  obstacles; short wall edges and actual acoustic paths are retained.
- Main-thread and DSP improvement is a hypothesis supported by reduced
  work, NOT proven until comparing profiler logs with actual gameplay.
- Source-level static tests and compilation do not verify audible quality.

## Verification

Regression runner: tools/VerifyAcoustics.ps1
Verified: 24 source/architecture checks passed.
Release build: 0 errors, 0 warnings.
Profiler: Enngard configuration has Profiler logging=true,
  Debug log=false. Logs are written every five seconds to
  Enngard/BepInEx/LogOutput.log under [acoustics profiler].
Test tasks still pending in-game: run the same indoor rain/window route,
  walk around fireplaces and two-room walls, confirm caves/reverb, visit
  the coastline and a dense base; compare profiler average, worst and
  audio-thread timings with the previous session.
