# 0.7.0 Occlusion geometry revision — 2026-11-21

## Changes from previously installed 0.7.0
- On a building/door change, recheck affected nearby active sources via the existing audio batch system instead of waiting for movement/fallback refresh.
- Priority is given to newly changed geometry and stale in-flight raycast results are discarded.
- The next computed geometry response reaches its target over an audio buffer rather than the normal 150 ms response.
- Removed the 0.3 arbitrary obstruction threshold from active multipath routing; wall presence now follows actual measured structural hits.
- No longer holds a previously clear/blocked value while waiting for corner probes. Applies current direct path immediately and adds physically verified alternate paths as soon as they are available.
- Known real wall edges shade even on the illuminated side using the existing Fresnel diffraction formula. Clearance uses wall plane and cached physical edge positions, not a timed A/B fade.
- Looping sounds close to real walls resample after 8 cm of movement (0.08 m) instead of 50 cm (0.5 m), still with existing audio update and a 100 ms minimum spacing.
- No new per-frame loops, polling system, Harmony patches, or background tasks.

## Verification
- 27 static/numeric checks passed.
- Release compile: zero errors, zero warnings.
- Source retained Valheim-specific room-scale adjustment.
- In-game sound and CPU performance require validation. A measured 0.27 ms before THIS revision does not establish the new overhead.

## Focused validation in ordinary survival gameplay
1. Stand near a constant sound (campfire or smelter). Place a wall between it and you. It should immediately become blocked after the asynchronous check (normally next audio updates), not wait for a 150 ms fade or for the player to move.
2. Remove the wall. The clear sound should return promptly.
3. Slowly walk behind an existing wall edge. Gain should vary with actual edge clearance/detour rather than jumping between predefined clear and blocked levels.
4. Listen for clicks, temporary disappearances, reverb discontinuities or unexpectedly shaded audio on the clear side of the edge.
5. Observe profiler CPU time near a busy base and while editing walls. Compare with the old 0.7.0 0.27 ms reading under similar conditions.

## Caveats
- Physics raycasts and known wall edges are sampled/cached, not infinitely resolved. A newly discovered edge still requires a batched geometry probe; sound gets its correct current direct transmission before that finishes.
- The illuminated-edge spectral representation is still a limited gain/lowpass approximation, not a full frequency-dependent wave solver.
- A freshly built wall may be applied one or more frames after construction because Unity jobs are asynchronous, not in the exact instant the mesh becomes visible.
- This version is an in-place local test patch for 0.7.0, not a public release.

## Rollback
Quit Valheim, then run: backup\V070_OCCLUSION_20261121\Restore.cmd
This restores the prior 0.7.0 source, packaged DLL, installed Enngard DLL and config from before the occlusion changes.
