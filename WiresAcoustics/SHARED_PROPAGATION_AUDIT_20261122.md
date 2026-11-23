# Wire's Acoustics 0.7.0 — established-systems architecture pass (2026-11-22)
Status: LOCAL DEVELOPMENT BUILD ONLY. Not a Steam Audio or Wwise equivalent; not yet tested inside running Valheim.

## External documented reference points
- Steam Audio source: https://valvesoftware.github.io/steam-audio/doc/unity/source.html (occlusion/transmission, path validation, moving geometry, HRTF)
- Steam Audio API simulation: https://valvesoftware.github.io/steam-audio/doc/capi/simulation.html (separate direct and path simulation, dynamic validation, diffraction approximation)
- Steam Audio Unity guide: https://valvesoftware.github.io/steam-audio/doc/unity/guide.html (scene geometry, material absorption, reflections, dynamic geometry)
- ITU-R P.526-16: https://www.itu.int/rec/R-REC-P.526-16-202511-I/en (diffraction models, multiple edges)
- Wwise spatial occlusion and obstruction: https://documentation.help/Wwise-Unity/pg__obs__occ.html

## Confirmed WA architectural limitations
1. Independent per-sound Corners.Record probe sets: identical verified wall geometry was rediscovered by separate sounds, causing redundant worker raycasts and delayed path discovery.
2. Batch combined cutoff parameters by weighted logarithmic averaging. Cutoff is not an additive quantity. For an incoherent set of paths, intensity/energy at each frequency adds, then the effective filter should be derived from those intensities.
3. Existing single-edge Fresnel solution is physically grounded for half-plane geometry but does not solve windows, corners in series, diffraction via multiple openings, dynamic material phase, or acoustic reflections. Its two-band fit is not a full-band field.
4. The 64 room rays and a single Unity AudioReverbZone are not an impulse-response reverb simulator.
5. Current user-created scenes cannot be assumed to have pre-baked pathing; Valve's precomputed pathing needs adaptation, not direct substitution.

## Implemented and validated in this pass
A. Shared completed edge-discovery snapshots (SharedEdges.cs)
- 128-entry bounded ring storing a completed snapshot, by hit position, normal, span, world generation.
- Reuse only with matching normal, <=8 cm spatial correspondence, cached span >= required span, and no nearby registered geometry changes.
- Never shares mutable arrays between sound filters. Copies all edge geometry and increments destination version, invalidating any stale in-flight per-sound probe messages.
- Only publishes after all four probe directions finish and the world has not changed nearby. Partial/ongoing probes are never shared.
- Integrated into Batch.Step before scheduling expensive new edge discovery; direct source/listener and actual indirect segments remain independently checked using worker raycasts.
- Clears at world/room reset (Room.Restore), global geometry invalidation (Batch.GeometryChanged) and Batch.Shutdown.
- Logs cache hit/miss/publish counters in the existing debug report. No additional Update hooks or polling.
- This is a conservative geometry probe cache, not an arbitrary scene-wide spatial index; its 8 cm key intentionally sacrifices some cache hits to avoid unsafe extrapolation.
B. Frequency-band energy composition (SpectralPaths.cs)
- The two-band acoustic energy of checked paths is now accumulated for 125 Hz and 2 kHz, and decoded into one gain and cutoff only after summation.
- Replaces cutoff geometric averaging for multiple validated openings, multiple corners, and direct+indirect path merges in Batch.cs.
- Retains the 1-pole lowpass+gain DSP, existing ray jobs and existing path direction logic.
- It assumes incoherent broadband path arrivals, as the prior energy-summation model already did; this is not a wavefield/phase simulator.

## Tests
- Build, net472: 0 warnings, 0 errors.
- TestSpectralPaths.ps1: identity, mixed-path high-band energy equality, low-band energy sum and output bounds PASS using compiled DLL.
- dev/SharedEdgesTests: compiles actual production SharedEdges.cs into a .NET 9 test runner with deterministic geometry/Unity stubs; checks unfinished probes excluded, matching probe copies, copy isolation, spatial/normal/span rejection, unrelated change retention, nearby change invalidation and reset PASS.
- Existing compiled FresnelEdge and Underwater numerical regression tests PASS.
- Direct attempt to run SharedEdges tests via PowerShell reflection into the Valheim DLL outside Unity failed due Valheim assembly type initialization incompatibility, not a tested in-game result. Production SharedEdges itself built under the game's reference assemblies without warnings.
- STILL REQUIRED: actual Valheim in-game audio/cpu profiling, door/window, placed wall, terrain change, two adjacent sources, source-motion and streamed zones.

## Untouched or still incomplete versus mature spatial engines
- Dynamic full geometry ownership, a complete topology graph of rooms/portals, multiple-edge diffraction/UTD, coherent sound fields, real-time early reflections and multi-tap IR convolution reverb, full measured HRTF, broad-band material transmission, extended sources, phase/diffuse arrivals.
- Converting this game mod into a complete Steam Audio/Wwise-class engine cannot be represented honestly by 1-2 source edits. Native simulation integration requires native binaries, Unity compatibility, material/mesh construction, source routing and runtime benchmarks.
- The existing 1-second stationary room and 2-second loop safety rechecks remain. Removing them reintroduces missed updates from third-party geometry changes outside managed hooks.

## Rollback
Close Valheim. Execute backup\SHARED_PATHS_20261122\Restore.ps1 to restore all source files and the previous installed/package DLL.
