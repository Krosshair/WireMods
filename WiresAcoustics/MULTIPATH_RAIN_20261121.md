# Wire's Acoustics — multi-window transmission and sheltered rain
Date: 2026-11-21
Status: unreleased 0.6.0 test build based on shared-systems refactor
Rollback: backup/MULTIPATH_RAIN_20261121/Restore.cmd

## Multi-window sound transmission
- Keeps the published 0.6.0 rays: listener-to-source, each of the two highest ranked room openings, and existing wall-edge diffraction probes.
- For each independently ray-verified window route, computes opening transmission, diffraction bend loss, path length loss and arrival direction.
- When two openings are active, their squared gains contribute to summed acoustic energy. Low-pass frequency and obstruction blend by received energy.
- Direction is the normalized, energy-weighted sum of real opening directions. Opposing arrivals lower directional coherence instead of snapping to one winner. The original acoustic precedence and sound-source location system are retained.
- Single-window routes retain their original attenuation, cutoff and localization, and the existing best-corner comparison remains.
- No additional geometry queries, audio clips, source objects or audio-thread processing were added.

## Rain inside buildings
- The original Valheim weather/ambient loop continues to play; existing per-window external rain and wind sources remain unchanged.
- When EnvMan.IsWet() and the measured room has an actual built roof, smoothly preserve a minimum 0.72 gain and at least 6500 Hz cutoff for fully sheltered rain. This allows outdoor rain texture to remain present through the roof and walls.
- Uses the already sampled built-roof room rays to distinguish homes from natural caves. No new raycasts or material impact samples.
- This is filtered playback of the original rain recording. It does not synthesize distinct raindrop collision recordings for different roof materials.
- The wet-weather blend transitions smoothly using the existing AudioMan update; non-rain ambience retains the existing room attenuation.

## Tests and limits
- Regression runner: backup/MULTIPATH_RAIN_20261121/VerifyWindowRain.ps1
- 24 source-level and numerical sanity checks passed; clean Release build passes (0 errors; original CS0649 warning).
- Shared-systems parity checks continue passing, preserving plugin identity, 37 Harmony patch declarations and DSP processing loops.
- Actual in-world sound and performance comparisons remain untested; test against a rainy wooden house with one and two windows and a natural cave before considering a release.
