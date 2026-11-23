# WA architecture verification - isolated pass 2026-11-22
Status: local development revision only; not ready for player final acceptance test.

## Source-backed defect
Area.cs cached collider shapes under their owner transform instance ID.
GeometryEvents.cs invalidated a child build piece or destructible transform, but
Area.Invalidate previously only removed the changed object's key. This did not
remove a cached shape belonging to its parent (such as an aggregated object or
prefab root with the moved/destroyed child collider). Affected sounds could be
marked AreaKnown=false, yet immediately reload the STALE parent bounding box.

## Established-engine comparison
Valve Steam Audio documentation separates scene geometry from acoustics and
requires committed updates to geometry; dynamic scene objects must propagate
their changes to the simulation. The same correctness requirement applies
to WA's cached source dimensions. Reference:
https://valvesoftware.github.io/steam-audio/doc/capi/guide.html
https://valvesoftware.github.io/steam-audio/doc/unity/guide.html
This does not make the cache equal to Steam Audio; it enforces a necessary
correctness property: a geometry change invalidates all dependent cached shape
measurements before reuse.

## Patch #1: Area.Invalidate
- Invalidate owner AND every ancestor's cached box, iterating the transform
  parent chain (O(hierarchy depth); not O(all Shapes)).
- Preserve current targeted iteration through live sound filters and invalidate
  each cached ShapeRoot key affected by the changed transform hierarchy.
- No extra Update/LateUpdate/FixedUpdate/polling, no new physics raycasts.
Validation performed immediately:
- production net472 build 0 errors, 0 warnings.
- dev/AreaInvalidationTests/RunSourceTest.ps1 extracts the ACTUAL production
  Area.Invalidate method, compiles it with deterministic Transform hierarchy
  stubs, validates ancestor, nested source, sibling and independent object cases.
- Reviewed against dynamic-geometry update contract above.

## Patch #2: GeometryEvents.SceneryDestroyed
- Existing Piece, Door hooks already called Area.Invalidate.
- Destructible.DestroyNow's Prefix only issued acoustic geometry change;
  now also invalidates the destructible's shape cache BEFORE destruction.
Validation performed immediately:
- net472 build 0 errors, 0 warnings.
- static inspection confirms the exact Harmony event path and that no global
  shape-clear or redundant per-frame pass was added.

## Combined independent regression suite
dev/VerifyEveryImplementation.ps1:
- independent digital DSP renderer parity 354 cases (44.1/48/96/192 kHz)
- diffraction-to-DSP spectral fit 38 cases
- Fresnel, underwater, spectral parallel+serial propagation
- shared edge geometry cache deterministic tests
- meter concurrent test 100,000 publications, overflow accounting
- acoustic trace synthetic telemetry tests
- production method source-extraction Area.Invalidate hierarchy tests
RESULT: all passed. This verifies implemented mathematical and code invariants;
does NOT prove exact acoustic equivalence with Steam Audio or Wwise.

## Still incomplete
- Source HRTF uses procedural cues, not actual SOFA HRTF data.
- Direct + multi-edge propagation remains a mixed approximate diffraction model.
- Portal identification is inferred, not an explicit room topology graph.
- Reverb uses geometry-informed parameterized AudioReverbZone, not measured IR or
  real-time early reflection paths.
- In-game sound, actual CPU, memory, conflict and native Unity behavior untested.
The requested absolute standard of no weaknesses cannot be established by
isolated mathematical and synthetic tests.

## Rollback
Close game and run backup\AREA_GEOMETRY_RECHECK_20261122\Restore.ps1
