# Wire's Acoustics — unified environmental audio implementation

Development version: 0.6.0 (unreleased changes only)
Status: Source updated, Release compiled successfully (0 warnings / 0 errors), package DLL and Gale Enngard DLL installed and verified identical.
Static QA: 31 source/design assertions passed; no new independent Unity Update/LateUpdate/FixedUpdate methods.
Installed DLL SHA256: 1ECE2E8BA4EF8FDA960B7B26A75AB7E1E707D1893F25A27D03FAD1B4DD75EA3E
Gameplay listening and frame/audio profiling: NOT TESTED.
Backup before latest geometry/profiling pass: backup/APERTURE_PROFILER_20261121/
Rollback: backup/APERTURE_PROFILER_20261121/Restore.cmd
Profiler: Enngard BepInEx/config/wire.valheim.acoustics.cfg - Profiler logging = true, Debug log = false
Profiler output: Enngard BepInEx/LogOutput.log, [acoustics profiler] every five seconds.
Regression script: tools/VerifyAcoustics.ps1

## Implemented

### AcousticModel.cs (new common audio policy)
- Sound geometry/profile enum: point, aperture, line, diffuse.
- Shared per-source audio configuration, native Unity reverb routing, spatial-HRTF/filter handling.
- Shared room-reverb send math for normal ZSFX.
- Aperture loudness based on sqrt(area), approximately proportional to radius. Independent rain copies use distinct loop phases instead of coherent stacking.
- Environmental sources are configured dry (rain, wind, shoreline); only ordinary room SFX use the full room response.

### OpeningRegistry + Portals + Room + GeometryEvents
- Opening candidates come from existing room geometry rays, requiring actual adjacent built surfaces.
- Each candidate carries a structural collider identity and wall/support estimate.
- Registry preserves opening identity and world-space emitter position across intermittent room-ray misses.
- Opening audio is attached to one registry source ID; no already-playing sound may retarget to a different source.
- Source retirement occurs on destruction of its supporting collider, local door/build events after a grace period, or long-distance unloading. Sources cannot reuse a live audio slot.
- When source slots are exhausted, distant sources can be retired before nearer new openings are admitted.
- Window playback mix is gated by an overhead building cover check (one bounded nonalloc ray query per completed room measurement), not the room enclosure percentage that fluctuates as you step near openings.
- Construction and door invalidation is routed through existing event hooks.

### Ambience + Ocean + Occlusion
- Window emitters use Unity 3D positional rolloff at fixed world positions.
- Sconces/ordinary SFX retain their own normal audio behavior.
- Reverb routing of native weather/wind sources is snapshotted and restored when disabled.
- Window and ocean sources both use AcousticModel.Render rather than independent HRTF/filters.
- Ordinary SFX reverb send uses AcousticModel.RoomSend.
- Existing ocean shoreline region tracking preserved; its outdoor copies also bypass Unity room reverb zones.
- No new MonoBehaviour Update/LateUpdate/FixedUpdate, coroutines or polling loops.

## Verified with source/game evidence

- Decompiled vanilla AudioMan shows original m_ambientLoopSource.bypassReverbZones = true already. Therefore a supposed fix consisting only of setting this flag would be misleading. Native ambience reverb perception may come from the recording, mixer routing, duplicated spatial copies, or another audio path.
- Static audio-source position is assigned once on initial play for window copies. A source-ID mismatch fades the old copy rather than repositioning it.
- A sparse room-ray miss alone no longer expires its persistent opening identity.
- Compiles against the installed Valheim managed assemblies and the current BepInEx dependencies.

## Explicit limitations / NOT verified

1. Aperture geometry now uses one nonallocating collider-overlap broadphase around the candidate, projects structural collider bounds into the local wall plane and estimates the opening between its bordering pieces. A short ray validates the resulting center. The previous 9x9 (81-synchronous-ray) geometry raster was removed. Irregular openings are still bounded-area approximations, not exact polygons.
2. New windows not previously observed may still be discovered after moving. Their audio enters as a new source; no current system can guarantee pre-discovery of all windows in an arbitrary building.
3. The normal Occlusion/Batch/Corners/Diffraction solvers are now shared by positioned window ambience and ordinary SFX. Both legs of every persistent aperture route are checked, and source-specific route selection plus energy/cutoff/precedence mixing are centralized. Physical ray/edge/room geometry producers remain specialized by design, not duplicated.
4. Native ambience already bypasses Unity's reverb zone. No runtime evidence yet proves that the perceived 'wet' rain is fixed, or rules out the ambient audio mixer and the rain recording itself.
5. Runtime listening, Harmony patch registration, and A/B performance tests are NOT completed. Compilation and 31 static assertions validate implementation structure, not audible results. Profiler logging is enabled for the next Enngard session at five-second intervals, with one-in-sixteen audio callback samples.

## Gameplay acceptance checks

- Stand still beside the window and let several room scans finish: no source ID churn and no changing emitter position.
- Walk sideways, toward/away, and behind building pieces: continuous Unity distance behavior, no source teleport. See debug logs for newly detected sources.
- Walk between two separated windows: both should contribute naturally, not silently replace each other.
- Open/close a door; destroy/rebuild supporting pieces: affected sources should retire/appear, unrelated nearby windows should remain.
- Turn Room reverb off and on: test whether perceived rain wetness changes. If it does while all sources bypass zones, investigate mixer/other sound path, not the aperture DSP flag.
- Visit shoreline, island and open ocean: no new regression.
- Toggle Enabled; change world; confirm reverb flags and source objects reset.
- Measure main-thread cost with debug on and off; test dense bases with 8 active windows, several sconces and ocean waves.

## Diagnostics
General / Debug log defaults to off. With it on, source birth/retirement and prevented slot swaps are logged. Reverb routing is deliberately explicit.

Version is deliberately not incremented until an actual published release is prepared.
