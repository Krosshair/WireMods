# Superseded by VOLUMETRIC_SOURCE_OCCLUSION_20261122.md

# Wire's Acoustics 0.7.0 — Partial Occlusion Root-Cause Correction
Date 2026-11-22
Status: LOCAL DEVELOPMENT; NOT gameplay-validated, NOT a release.
This document replaces unsupported claims that the old all-or-nothing
occlusion issue had already been solved by spectral model rework.

## Root cause confirmed in source
Before:
- Batch.Walls sampled three world rays (center, +8 cm side, +8 cm up).
- Occlusion.Median computed the 125-Hz gain for all three, chose ONLY
  the middle one, threw away the other two frequency-dependent transfers.
- A sound immediately changed from the clear ray's gain/cutoff to a whole
  wall's gain/cutoff once two of three rays crossed that collider.
- DSP smoothing merely delayed that A/B step; it did not model partial
  source occlusion.
- Initial sound Starts used the same median, and if any sound source face
  was unobstructed, snapped the entire sound to the clear spectrum.
- EdgeCanOverride prevented a validated single-edge Fresnel solver from
  correcting a fully blocked sound without a clear corner route.
- Portal entrance, source and corner legs also used median decisions.

## Concrete correction
- Added src/ThreeRayTransmission.cs. A small finite-aperture quadrature
  computes weighted average 125-Hz/DC energy and 2-kHz energy across the
  ALREADY-CAST three rays, then calls the same verified discrete digital
  two-stage DSP inverse once. Center weight=0.5; two offset rays=0.25 each.
  Weighted coverage of rays affected by geometry (0, .25, .5, .75, 1)
  is explicitly logged. Gain is not linear interpolation of amplitude.
- Batch.DirectWalls calculates independently measured 3-ray spectra,
  retains a real obstructing Block for edge metadata, and uses weighted
  spectra for direct sources, both entrance/source legs of portal routes,
  and both legs of corner routes.
- Removed the old median selection and redundant winner comparator.
  Indirect paths cascade their two measured legs and diffraction, then
  compete/merge by spectral energy, not an arbitrary wall-state flag.
- Starts initial playback uses the same measured spectrum: no clear
  'face' override. A visible source face adds an equally weighted
  alternative emitting region, preserving partial occlusion.
- Occlusion.Resume accepts a measured direct spectrum and coverage rather
  than re-deriving a binary wall state from representative metadata.
- Validated single-edge Fresnel can refine a sound even while all three
  rays remain blocked. It is disabled where competing validated
  multipath routes would make a single ideal half-plane misleading.
- Debug log includes 'coverage=' for each source and existing actual
  DSP gain/cutoff/route measurements.

## Reference approach
Valve's Steam Audio distinguishes single raycast (binary) from volumetric
occlusion (partial, multiple samples), and applies wall transmission only
to the occluded portions of a source:
https://valvesoftware.github.io/steam-audio/doc/capi/simulation.html
https://valvesoftware.github.io/steam-audio/doc/unreal/source.html
Wwise differentiates obstruction, occlusion, diffraction and portals:
https://documentation.help/Wwise-Unity/pg__obs__occ.html
WA's 3-ray weighted transmission is a LOW-SAMPLE approximation, NOT
Steam Audio's sphere-volume algorithm. Real volumetric sampling depends on
the source's apparent extent/radius and can use many samples; WA's
current three offset rays cover a fixed ~8cm cross-section near source
AND listener and cannot represent precise coverage of a 3m-wide emitter.
This is a known remaining weakness.

## Verification
- Actual production src/ThreeRayTransmission.cs linked into the
  deterministic dev/PartialOcclusionTests harness.
- 1600 cases at 44.1/48/96/192kHz, mixed clear/wall, side-ray
  coverage, monotonic behavior, face-region contribution and 2kHz parity.
- Existing digital two-stage filter has intentional 20kHz bypass;
  resulting 2-kHz representation residual may be <= 0.005 intensity
  near that discontinuity. Cannot exactly fit all spectra with one
  gain + one cutoff, and this test explicitly tolerates that bound.
- All existing production-source geometry, Fresnel, diffraction, HRTF
  metadata, environmental and 100000-packet concurrency checks pass.
- No extra raycasts, no Update/FixedUpdate/LateUpdate methods,
  no new polling, no extra work inside the DSP per-sample loop.
- GAMEPLAY AUDIO AND CPU PROFILE STILL UNTESTED.

## Debug interpretation
'coverage=0.00' means none of the three acoustic rays detected
obstructing geometry; 'coverage=0.25, 0.50, 0.75' indicates sampled
partial geometry blockage; 'coverage=1.00' all rays intersected
obstruction. This is an APPROXIMATE sampled coverage fraction, not
an exact fraction of actual wall silhouette or field amplitude.
path=single-edge-fresnel indicates a separately verified 1-edge
model overrides sampled transfer in a continuously modeled geometry.

## Remaining acceptance
- Verify physical wall transitions in an actual dynamic base with
  doors, corners, partially built walls, trees, roofs, narrow openings.
- Validate observed spatial behavior against numeric coverage logs.
- Profile total physics-job and DSP CPU cost; median elimination changes
  source calculation but does not add ray jobs.
- Consider future SOURCE-SPHERE volumetric occlusion with configurable
  radius and adaptive sample count (Steam Audio equivalent), after
  current behavior and budget are independently measured.
- Full Wwise room geometry and impulse-response reverb remain distinct
  major features. No 'perfect' / no-regressions certification.

## Rollback
Close Valheim; run backup/PARTIAL_COVERAGE_20261122/Restore.ps1.
