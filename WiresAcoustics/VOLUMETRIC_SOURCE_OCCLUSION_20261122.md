# Wire's Acoustics 0.7.0 — Source-Volume Occlusion Engineering Build
2026-11-22. Local source-level engineering. NOT a published public release.

## Objective
Replace the known A/B ray median / small fixed-offset partial visibility
approximation with a Valheim-appropriate, geometry-informed volumetric
source occlusion solution, using Steam Audio volumetric occlusion as the
algorithmic reference rather than claiming identical native rendering.

Reference: https://valvesoftware.github.io/steam-audio/doc/unity/source.html
- Steam Audio volumetric mode traces multiple rays to points in a source
  sphere and computes partial visibility; frequency-dependent wall
  transmission applies to the obstructed portions.
Reference: https://valvesoftware.github.io/steam-audio/doc/capi/simulation.html

## Design implemented
1. Actual 3D emitting volume. src/SourceVolume.cs
   - For looping sounds with measured, local bounding boxes from
     Area.Measure: sample deterministic low-discrepancy 3D positions
     inside the sound owner's ROTATED and SCALED local box. Points
     cannot leak outside that collider AABB due to a projected-box error.
   - Localized authored one-shots (chest lid, door hinge, footsteps) and
     sound sources without a valid collider: physically finite 0.30 m
     radius sphere around the actual audio source position, not a whole
     object's bounding box or a zero-width binary ray.
   - Sample count adapts to apparent source size and listener distance,
     bounded by user setting: 0 / 4 / 8 / 16 / 32 additional rays.
     Default cap 16; 32 optional high quality. No extra work if
     occlusion is disabled.
   - Stratified Halton sequence (base 2/3/5) is deterministic for
     stable geometry; no random per-frame jitter/flicker.
2. Batched ray physics integration.
   - Batch.Step requests additional source-volume rays in the SAME
     existing Unity RaycastCommand.ScheduleBatch job.
   - Extra rays are injected after the direct 3-ray bundle. Portal and
     corner ray offsets are shifted by VolumeN and continue to use
     their own measured legs.
   - Batch.Apply reads only the current job's bounded samples, converts
     each one into source->listener transmission material gain and
     2-kHz spectrum, averages ENERGIES, and passes ONE fitted two-pole
     filter state to the existing AcousticFilter audio callback.
   - Weighted blocked fraction also gates indirect path energy; portal/
     corner routing does not suddenly appear at full gain when the
     first source sample is blocked.
3. New sounds and one-shots.
   - Starts.Schedule includes source-volume rays in its existing initial
     job, accounting for both the center and optional source face.
   - Starts.Step applies the same sampled spectrum BEFORE the sound
     is released for playback, including absent AcousticFilter
     components via 0.30m fallback sphere.
4. Continuous single-edge diffraction, not an A/B bypass.
   - src/VolumeFresnel.cs implements real half-plane wave transfer for
     each physically sampled source point.
   - ContinuousEdges.EvaluateVolume integrates spectral ENERGY of
     the entire source volume whenever a validated SINGLE wall edge
     and no competing independent routes are present.
   - Runs only while processing completed Batch work, not in a
     new per-frame loop or audio callback.
   - Samples already on the listener's side of the wall are direct
     clear sound rather than failing the calculation for the whole
     source.
   - The old point-source Fresnel update cannot replace a measured
     volume spectrum in Directions. Its point-source behavior remains
     available when volumetric sample count is zero.
5. Diagnostics and tuning.
   - New BepInEx setting: [Tuning] "Source-volume rays", default 16,
     allowed 0/4/8/16/32.
   - Existing debug log emits per-source coverage and volumeRays, plus
     actual DSP gain/cutoff, geometry revisions, route type, RMS and
     the existing 3-second overall source-volume ray count.
   - New acoustic path "volume-fresnel" distinguishes wave-integrated
     source volume from basic "direct-partial".

## Architectural invariants
- 0 new Update / LateUpdate / FixedUpdate, coroutines, polling loops.
- 0 new operations inside OnAudioFilterRead / sample-by-sample DSP loop.
- All physics checks run in existing scheduled jobs, bound to
  SourceVolume.MaxSamples=32 per source.
- No per-sample audio-thread memory allocations, no new HRTF threads.
- Geometry remains event-invalidated; each source's dimensions come
  from the existing measured cached collider shape.
- No fabricated occlusion times, loudness or CPU performance figures.

## Verification (synthetic, not in-game)
A) Actual SourceVolume.cs linked into dev/VolumeSamplingTests and tested
   across multiple rotated, scaled object shapes, near/far distances,
   fallback spherical sources and 0/4/8/16/32 sample caps. Test points
   verified to lie inside local collider AABB (for shaped looping
   sources) or within a sphere of radius 0.30m (unshaped one-shots).
B) Actual VolumeFresnel.cs and FresnelEdge.cs linked into the same
   harness; 1805 transfer evaluations across half-plane wall edges,
   with continuous 3D-emitter field and no A/B threshold step.
C) dev/TestSourceVolumeIntegration.ps1 verifies actual source code
   ordering, job prefix offsets and 510 different combinations
   of portal count, edge count, volume ray count and new sound jobs.
D) Existing production-source tests preserved: 354 DSP parity,
   324 Fresnel geometry/material cases, 6060 wall transfer cases,
   400 corner serial transfer cases, 75 finite obstacles, portal
   topology permutations, area geometry, concurrent meters.
E) net472 project builds with 0 warnings/errors. Full suite passed.

## Practical differences from Steam Audio
- WA uses Valheim's Unity Physics colliders and source hierarchy, not
  Steam Audio's dedicated simulation geometry/traced triangle scene.
- The sample count is finite and adaptive (default cap 16), so very
  small gaps can still alias in 4-ray distant-source mode.
- A collider-derived local bounding BOX is only a simplified physical
  emitter; irregular meshes, cavities and directional radiators
  are not modeled as actual radiating mesh surfaces.
- The two-band DSP approximates spectral magnitude, not full 1/3
  octave transmission, phase coherence, IR convolution or true SOFA HRTF.
- One-edge Fresnel is used only after a verified ONE-edge geometry;
  general multiple-edge wave propagation is a separate problem.
- Increasing from 3 rays to as many as 19/35 rays affects physics
  job throughput and main-thread ray-hit parsing. Actual in-game
  frame CPU and audio perceptual quality have NOT been measured.
  No claims of perfect runtime sound or guaranteed performance.

## Release status and rollback
This is a development build for private verification, not a public
package ready for Thunderstore. Wait for real gameplay evidence before
claiming the A/B problem is fully eliminated in every Valheim scene.
Rollback while game closed:
backup/SOURCE_VOLUME_20261122/RestorePreviousDev.ps1
Dry-run check:
powershell -NoProfile -ExecutionPolicy Bypass -File
 backup/SOURCE_VOLUME_20261122/RestorePreviousDev.ps1 -DryRun
Full test:
dev/VerifyEveryImplementation.ps1
Finite static gate:
dev/VerifyReleaseCandidate.ps1
