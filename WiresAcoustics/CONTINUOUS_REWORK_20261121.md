# Wire's Acoustics 0.7.0: Continuous-acoustics development build — 2026-11-21

LOCAL EXPERIMENT ONLY. No public Thunderstore/Hexium release. Not tested in-game.

## Problem found
Old path propagation still contained discrete wall-hit classification, hard winner-takes-all corner choices, a 150ms timing glide, enclosure-dependent response thresholds, underwater Boolean immersion and an earlier single-edge prototype selecting nearest edge and hard-swapping transmission/diffraction. The continuous-looking interpolations could therefore inherit discontinuous inputs.

## Changes
- Rebuilt `ContinuousEdges` as a position-driven propagation evaluator. It uses geometry discovered by existing worker raycasts / measured edge probes. Its signed wall-edge clearance continuously controls diffraction, measured wall transmission and spectrum. The shadow-side transmission is coupled to a Fresnel-number-dependent strength that goes to zero at the boundary. Mathematically the lit and shadow responses meet at the same gain.
- All verified edges in the same cached wall record are now evaluated using a smooth path-detour weight, instead of instantly switching to whichever edge was closest. The arriving sound's HRTF direction becomes an energy-weighted point, and its room-reverb send follows the same evaluated gain.
- For blocked paths with multiple corners but no independent opening route, `Batch` now combines corner energies, frequency response, and arrival position. The existing portal scheme is preserved, including validation of routes, room measurements and extra-wall safeguards.
- `Room` enclosure response and reverb-tail response now vary continuously with measured enclosure change magnitude, rather than choosing one of two speeds at an arbitrary 12% change.
- Outside ambience attenuation now follows the full measured enclosure fraction instead of flat zero until 35% and full by 70%.
- Underwater *source and listener* acoustic losses are depth-dependent fractions (0..1) over a 0.3m transition and combine continuously rather than toggling fully on/off. Spatial depth, not a clock-based Boolean fade, drives the listener fraction. Global and per-source muffling are mixed using source/listener immersion.
- Kept the existing budget, BepInEx configuration, sound lifetimes and scheduled acoustic jobs; no additional Unity Update hook, coroutine, global scanning system, or audio-thread allocation path.

## Remaining limits / do not advertise as universally continuous yet
- A **never-before-discovered** wall still needs asynchronous raycast/probe discovery. The old direct-path fallback can therefore step on that first encounter; true initial-geometry continuity would require additional scene geometry acquisition/indexing.
- Moving structures, multiple solid walls, and mixed window/portal routes continue to use the conservative sampled solver and require actual runtime test.
- Room enclosure is estimated with 64 geometry rays, and its observations still have spatial sampling error. Purely continuous scalar response does not remove that.
- The two-pole single-cutoff DSP is not full frequency-resolved diffraction/impulse-response processing. The Fresnel transmission crossover is an approximation and needs listening validation.
- Evaluating cached edge geometry in the existing direction pass has a CPU cost. Compare dense-base WiresProfiler numbers and 30/60/120 FPS. No runtime profiling has been performed.
- Some Boolean flags are intentionally retained for discrete *existence, applicability and work scheduling*, not to represent the acoustic level itself.

## Static validation
- Release .NET Framework build: 0 warnings, 0 errors.
- Reflection invoked actual compiled continuous Transfer method at the signed edge boundary for low-to-high wall transmission; gain matched on both sides and numeric ranges validated.
- Reflection invoked compiled Underwater.Apply for fractions 0, .1, .25, .5, .75, .9, 1; amplitudes decreased smoothly with no out-of-range values.

## Essential survival-mode regression checks
1. Stand near a campfire/smelter; move slowly across the corner of ONE pre-existing wall, and reverse several times. No audible after-the-fact binary transition after an edge is discovered.
2. Repeat behind a wall you haven't previously approached to distinguish first-discovery delay from the cached model.
3. Test between two walls; door/window and multiple opening paths; place/remove a wall; rotating pieces. Look for sound leaks or incorrect source position.
4. Wade into water and back out, and check a submerged sound. No instantaneous full muffling/unmuffling at 30 cm depth.
5. Walk between indoor/outdoor areas; check reverb and ambience continuity. Reverb decay should remain physically gradual.
6. Check profiler around a dense base; compare sound, CPU, audio pops and spatial direction to the previous build.

## Rollback
Close Valheim and run `backup\CONTINUOUS_REWORK_20261121\Restore.ps1` in PowerShell with an appropriate execution policy. This restores original source and the installed and packaged DLL.
