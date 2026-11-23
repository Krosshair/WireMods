# Wire's Acoustics 0.7.0 — FHWA-grounded wind propagation
Date: 2026-11-22
Status: unpublished release candidate. Gameplay and active rain tests remain pending.

## Research basis
FHWA Highway Traffic Noise Measurement Handbook, section 4.1.1 / tables 4-1 and 4-2:
https://www.fhwa.dot.gov/Environment/noise/measurement/handbook.cfm
These tables reproduce road-traffic sound-level *model estimates* from NCHRP Report 791 under wind and without noise barriers or a vertical temperature gradient.
Distance: 15.2, 30.5, 61, 122, 244, 488 metres.
Wind along source-to-listener vector: 2.5 and 5.0 m/s, both upwind and downwind.
Listener heights: 1.5 and 4.6 metres above ground.
Ground: two separate tables for acoustically hard vs acoustically soft surfaces.

Critical limitation: These are not measurements of arbitrary point-source Valheim noises. Source height, spectrum, actual ground along the path, temperature lapse rate and atmospheric turbulence also matter. The conversion from Valheim's normalized wind intensity to physical wind speed remains approximate (12 m/s at intensity 1); the FHWA data are clamped above 5 m/s and 488 m rather than extrapolated. Therefore this is an evidence-based, cheap approximation, not a full physically exact solver.

## Previous model
Hard coded +3 dB downwind at 7 m/s, -10 dB upwind at 4 m/s, and a 15–112 m linear ramp. No ground or listener-height dependence. Listener's already-measured wind cover scaled the result.

## New model
- src/WindPropagation.cs: 16 short immutable table rows for hard/soft ground, 1.5/4.6m receiver, upwind/downwind, 2.5/5m/s, each at 6 distances. Linear interpolation of distance, wind magnitude, nearby ground softness and receiver height; small near-zero fade; no extrapolation.
- src/Room.cs: classify local ground porosity from alpha and floor distance from the existing room's 64 ray results. No extra physics. Smooth across room measurements, default to neutral ground and 1.5m height when not yet measured.
- src/Directions.cs: at the existing 4 Hz sound-direction slow pass, replace old fixed numerical WindCarryDb with the FHWA interpolation. Still uses projected wind speed and previously measured Ambience.WindExposure. Applied to positioned world SFX, voice, machines, fires, creatures; unpositioned beds/music/UI unaffected.
- Existing geometry occlusion, reverb, Doppler, sound-distance falloff, randomized independent loop playback and rain/biome treatment remain unchanged.

## Example values at 122m, 5m/s and exposed, source assumptions from FHWA
Hard ground, 1.5m ear: -11 dB upwind, +1 dB downwind.
Soft ground, 1.5m ear: -9 dB upwind, +14 dB downwind.
Hard ground, 4.6m ear: -4 dB upwind, +1 dB downwind.
Soft ground, 4.6m ear: -8 dB upwind, +10 dB downwind.
Being sheltered scales all of these continuously: 100% shelter -> no wind effect. All sound distances and wall occlusion still work.

## Verification
- Source-extracted FHWA wind model tests: 25 numeric tests, upwind/downwind, each ground and height, distance/wind interpolation, clamping, disabled and complete shelter. PASS.
- Regression integration: PASS; source connects live Room measurements, existing wind exposure and 4 Hz positional loop.
- dotnet build net472 Release: 0 errors, 0 warnings.
- Full VerifyReleaseCandidate: PASS, 51 production .cs, source+numerical regression; final report RELEASE_CANDIDATE_VERIFICATION_20261122.txt.
- On KajetanGOAT, synthetic local C# benchmark: ~0.043 microseconds per WindPropagation.Correction (300,000 calls including warmup), 0.034 ms CPU per second assuming 200 active positioned sounds at 4 checks/sec. NOT an in-game profiler reading.
- In-game audibility and active rain: NOT YET VALIDATED on this exact build.

## Artifacts
- Pre-change backups: backup/EMPIRICAL_WIND_FHWA_20261122
- Restore script: backup/EMPIRICAL_WIND_FHWA_20261122/Restore.ps1 (only when game is closed)
- Installed SHA256 (Enngard + package DLL): 06588ADC6022D60D0A4B0B9C279BF2FBDEB1B6892FEE43AA346437526BEADDD9.
- Release staging: staging/WiresAcoustics-0.7.0-RC-empirical-wind-rain-pending.zip, SHA256 422B4E2903FBE1432E7D22FD854D889484B66D534DD67364AC34B05202E9B497 (5 required root entries verified).
- No public release or version bump.
