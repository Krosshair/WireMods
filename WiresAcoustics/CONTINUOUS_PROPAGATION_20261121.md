# Continuous propagation architecture experiment (2026-11-21)
Local-only revision of Wire's Acoustics 0.7.0. Do not publish without in-game tests.

## Main change
- `ContinuousEdges` holds measured single-wall transmission and previously raycast-verified edges separately from the current listener position.
- The existing `Directions.Step` supplies listener/source coordinates. A cheap calculation evaluates the diffraction response and arrival direction on movement. No extra MonoBehaviour Update, raycast, coroutine, or timer was added.
- Existing batched jobs continue to discover wall intersections, corners, and validate paths. Edges are invalidated on relevant world edits.
- A validated isolated wall edge can respond continuously to movement without waiting for a fresh direct-path raycast.
- The model only overrides a completed, single-wall corner path (or clear direct path with a previously validated edge). Portal routes, additional walls, terrain, moving sources, and unknown edges fall back to the existing solver.
- Previously accelerated geometry edits and one-buffer click-safe response are preserved. Raycast movement thresholds stay small for fallback and grow only after verified edge evaluation.

## Known limitations and release blockers
- First entry into a previously unseen wall shadow still requires discovery and probe completion. This is NOT a universal solution to that latency.
- Doors/openings and multi-wall paths are still sampled rather than fully continuous, and portal routing is intentionally not overridden.
- One-edge diffraction + wall transmission is a bounded approximation, not a frequency-domain wave simulation.
- Reverb send uses batched geometry updates, not per-position evaluation.
- CPU and audible quality have NOT been verified in game.

## Validation steps
1. Constant smelter/campfire behind ONE existing plain wall; walk slowly behind and out of its measured edge repeatedly, including switching direction.
2. Compare crossing an edge that was previously discovered to crossing a fresh, previously unmeasured wall.
3. Test wood/stone walls, windows/doors, two walls, door changes, rotated pieces, and teleporting; ensure fallback works without leaks.
4. Recheck lowpass, gain, HRTF direction, route localization, audio pops, and performance in a dense base.
5. Compare WiresProfiler total CPU with previous baseline before release.
6. Rollback: stop Valheim and run backup\\CONTINUOUS_PROPAGATION_20261121\\Restore.ps1.

## Static verification
- .NET Framework Release build with 0 errors and 0 warnings.
- No new engine hooks introduced; source and output hashes should be compared after deployment.
