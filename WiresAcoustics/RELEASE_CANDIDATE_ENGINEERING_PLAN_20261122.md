# Wire's Acoustics 0.7.0 — Release-Candidate Engineering Plan and Result
Date: 2026-11-22
Owner: Kajetan
Baseline: backup/RELEASE_ENGINEERING_20261122 (ALL 43 production .cs files, original compiled/installed DLL and project).
Status: static-correctness RC, not a certified, acoustically perfect public release.

## Stop the infinite cycle
Definition of done is tied to the actual purpose of WA and a finite set of acceptance gates.
Steam Audio and Wwise are references for modeling principles, not a requirement
to copy every native engine subsystem. Adding convolution, SOFA HRTF or authored
room/portal graphs is a distinct feature scope that requires its own runtime
validation, CPU/memory budgets, compatibility tests and licensing review.

## Goal
A correct, stable, predictable, low-overhead Valheim spatial-acoustic system
with continuously varying attenuation based on geometry and material,
reasonable low-cost diffraction, indoor/outdoor room characteristics, clean
source positioning, reliable first-sound behavior, no abrupt muffle toggles,
and interpretable diagnostics.

## Keep (and why)
- Room's existing batched 64-ray approximation, subject to documented
  aperture-resolution limitations; no live scene-wide triangle traversal.
- RayJobs' Unity physics jobs, shared edge cache with event invalidation,
  path recheck on world changes and source move/door operations.
- Single-edge Fresnel in verified geometry plus conservative fallback on
  uncertain multiple-edge paths.
- Low-cost digital two-pole DSP matched at DC and 2 kHz. This is a two-band
  transfer approximation, not a full acoustic spectrum or phase solver.
- Procedural headphone spatialization and Unity reverb, explicitly labeled
  approximate; preserve existing Valheim compatibility and performance.
- No new Update/LateUpdate/FixedUpdate/coroutines/polling; all propagation
  work stays in existing event triggers/managed periodic jobs.
- 128 configured real voices pending actual game CPU measurements: changing
  global Unity mixer count without comparing audible dropout and CPU is risky.

## Corrected in this engineering pass
1. Occlusion.cs: independent spectral stages previously used cutoff=min(...),
   discarding the additional loss of other real walls, objects and terrain.
   - Wall layer accumulation now CASCADES frequency energy for serial walls.
   - Multiple obstacle diffraction transfers CASCADE.
   - Ridge and other bends CASCADE.
   - Soft/wall/terrain final attenuation CASCADES frequency energy after
     the strength policy is applied.
   - Preserve existing low-frequency scalar gain and feature strengths.
   - Verify actual production method via separate extraction harness with
     6060 combinations over 44.1, 48, 96 and 192 kHz and strengths 0.5/1/1.5, one through six serial walls, and disabled behavior.
2. Filter.cs: explicitly return from Process for zero-length/partial frames
   to avoid 1/frames and audio pointer state damage. No normal path changed.
3. Plugin.cs: debug description corrected from 14 to 24 audible sources.
4. Test infrastructure: OcclusionSerialTests included in the common
   VerifyEveryImplementation.ps1 suite. Test production source, not a
   duplicate handwritten version.

## Full source inventory (43 C# production files)
1. Model, propagation & geometry:
 AcousticMath.cs, Batch.cs, ContinuousEdges.cs, Corners.cs, Diffraction.cs,
 FresnelEdge.cs, Occlusion.cs, Portals.cs, PortalLoop.cs, Room.cs, SharedEdges.cs,
 SpectralPaths.cs, RayJobs.cs, GeometryEvents.cs, Area.cs.
 Retain with the corrections above. Remaining approximations are explicit:
 64-ray aperture resolution, four portal candidates, two frequency points,
 one verified single-edge Fresnel, parameterized late reverb.
2. Actual DSP, control & meters:
 Filter.cs, AudioMeterHistory.cs, AcousticTrace.cs, Diagnostics.cs,
 Budget.cs, Cost.cs, Directions.cs, Hearing.cs, Doppler.cs, Underwater.cs.
 Retain. Audio callback allocations/locks are avoided; meter is disabled
 outside debug, output is per-source pre-mix, not a recording of what is heard.
3. Sound source lifecycle, classification and effects:
 Starts.cs, Follow.cs, Spots.cs, Animated.cs, Equip.cs, Prefabs.cs,
 Spawned.cs, Stackify.cs, PlayerSounds.cs, VoiceChat.cs,
 AudioProvenance.cs, Menu.cs.
 Retain with existing source/owner attachment, startup delay, held sound
 initial raycast, fallbacks for missing companion mods.
4. Environmental sources:
 Ambience.cs, Indoor.cs, Mist.cs, Ocean.cs,
 VanillaShortAmbient.cs.
 Retain under existing event-driven updates. Direct global acoustic
 renderers for these sources are approximations, not native simulation.
5. Plugin & runtime:
 Plugin.cs, plus controls in the above units.
 Master switch/feature overrides and startup voice allocation must
 be checked against Valheim version and config in the final runtime gate.

## Acceptance gates with explicit stop conditions
A. [PASS] All C# source files compile net472 Release, 0 warnings/errors.
B. [PASS] Source-level DSP parity against actual discrete-time two-stage
   renderer at 44.1, 48, 96 and 192 kHz; spectral sum/cascade; Fresnel and
   diffraction geometry, source displacements; underwater boundaries.
C. [PASS] Geometry/cache tests: actual production shared cache, parent/
   child invalidation, opening connectedness, source movement and transform
   handling tests available; deterministic tests are not game physics.
D. [PASS] Audio diagnostic ring tested with 100k concurrent packet
   publications; test parseable source/geometry and transition traces.
E. [PASS] Production Occlusion.Layer, Muffle and Wall extracts evaluated
   by independent numerical reference, 6060 cases (this pass).
F. [PASS] No newly introduced MonoBehaviour per-frame hooks, coroutines,
   permanent polling, or audio-thread allocations in these changes.
G. [OPEN] In-game sample behavior: initial playback and door/edge movement,
   partial bases/open sky, real sound source positions, underwater transitions,
   HRTF direction, reverb.
H. [OPEN] Performance: compare CPU trace of WA disabled vs enabled in
   dense base, outside, rain. Consider both main thread and audio thread;
   log p50/p95/p99 and CPU spikes. No fabricated measurements.
I. [OPEN] Target game compatibility: startup sound holding and Valheim's
   actual Unity/BepInEx/Harmony callback behavior. Synthetic tests are not
   equivalent to executing the game.

Decision rule:
- For (A)-(F), stop changing unrelated code once green.
- For (G)-(I), do not claim pass from static simulation. These require
  independent runtime evidence, which can be obtained without a public release.
- Only fix failures tied to concrete, reproducible acceptance requirements;
  every change must include source audit, independent test, regression suite,
  rollback and recorded CPU behavior if the change affects the audio loop.

## Reference criteria
Steam Audio separates direct effects, propagation, reflections and
simulator threading:
https://valvesoftware.github.io/steam-audio/doc/capi/simulation.html
https://valvesoftware.github.io/steam-audio/doc/capi/integration.html
Wwise explicitly models rooms/portals, diffraction and transmission:
https://info.audiokinetic.com/gamesoundcon-2025

Neither reference certifies this mod's full parity. WA intentionally has
a smaller dynamic procedural engine optimized for Valheim rather than full
native ray-traced IR/HRTF implementation.

## Provenance / rollback
Run dev/VerifyEveryImplementation.ps1, then
dev/VerifyReleaseCandidate.ps1 for a reproducible report.
For baseline rollback when Valheim closed, use
backup/RELEASE_ENGINEERING_20261122/Restore.ps1.
Do not publish a public release until the OPEN runtime gates are closed.

## Controlled numerical performance measurement (synthetic, not game profile)
Using identical compiled test stubs and 100,000 occupied-wall transfer evaluations on the same PC, the more physically consistent two-band transfer costs approximately 2 to 2.5 times the previous min-cutoff approximation. This is an individual method microbenchmark, NOT a claim about Valheim frame times or audio callback CPU. Final renderer uses one spectral Decode after accumulating all transfer components, retains early exit for unoccluded paths, and introduces no new rays. Do not expand the simulation without validating whole-game budget; use profiler trace instead of extrapolating this test to frame time.
## Final performance refactor in the wall pipeline
Direct wall/obstacle/ridge physics now accumulates 125 Hz gain and 2 kHz energy as primitive values in Occlusion.Block and calculates a single effective cutoff per route. In contrast with the first revision, it no longer performs an inverse-filter fit after each collider. Source-level tests check 6060 combinations covering 4 output sample rates and 3 occlusion strengths. A noisy offline microbenchmark now measures roughly 1.6-1.7x old min-cutoff method cost per heavily obstructed evaluation, versus about 2-2.5x for the first mathematically correct implementation. This is not a game or audio callback profiler measurement. Cutoff fields were removed from production Block and replaced with cumulative band-energy fields. No new raycasts or per-frame hooks.
