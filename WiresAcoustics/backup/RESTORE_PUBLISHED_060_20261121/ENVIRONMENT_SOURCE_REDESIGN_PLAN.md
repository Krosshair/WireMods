# Wire's Acoustics — Environmental Audio Source Redesign

Updated: 2026-11-21
Status: Implemented, Release build successful (0 errors, 0 warnings) and installed in Enngard; in-game acoustic and performance acceptance tests still pending.
Verified latest installed SHA256 (package and Enngard): 0FE079A13FC3672892347C64154C365F3790735D7FD7EF7A1EA5B9B6FB36A711.
Superseding technical implementation report: UNIFIED_AUDIO_IMPLEMENTATION_20261121.md (newer unified audio iteration).
17 source assertions passed and 0 warnings / 0 errors; runtime audible/performance tests still not performed.
Important: exact opening polygon reconstruction and complete legacy SFX propagation migration are NOT finished; see implementation report.
Release policy: Keep Plugin.Version and package manifest at 0.6.0 until an actual release is authorized.
Starting backup: backup/ENV_SOURCE_REDESIGN_20261121 (full src, original package and Enngard DLL).

## Architecture

- [x] Separate environmental source identity from transient geometry ray observations.
  - `src/OpeningRegistry.cs` keeps 8 bounded world-space opening slots.
  - New readings match existing slots by world position + direction, not apparent area rank.
  - Ray misses do not immediately remove a tracked source.
  - Geometry changes and direct-path visibility participate in invalidation.
  - Old slot cannot be recycled until its audio has finished fading.
- [x] Preserve work without any new MonoBehaviour Update, FixedUpdate, LateUpdate or coroutine.
  - Opening observation and batched line-check jobs run on existing room measurements.
  - Cheap gain/direction calculations reuse existing AudioMan.Update.
- [x] Native arrays are reusable and disposed on reset/shutdown.
- [x] Reset sound-source state when the game world changes, without carrying old window/ocean positions into the next world.
- [x] Debug-only summary reports opening IDs, position, observation age, obstruction and shoreline source states.

## Rain / window ambience

- [x] Remove two-opening selection and rank-swapping audio sources; support up to 8 simultaneously.
- [x] World-space source anchors persist through sparse room-ray misses.
- [x] Current listener distance and angle determine direct sound on each normal audio update.
- [x] Replaced the unstable binary opening obstruction state with ordinary Unity 3D spatial attenuation, as for a brazier. The diffuse indoor rain layer remains separately muffled.
- [x] A diffuse, muffled rain/biome bed persists behind walls.
- [x] Separate source creation/retirement fades from listener-motion attenuation; the latter runs in Unity's audio engine.
- [x] Removed combined amplitude normalization: larger and multiple openings contribute more audible rain. Native per-source 3D rolloff handles their distance.
- [x] Retire on nearby geometry changes or when outside acoustic range, not on individual missed room-ray samples.

## Ocean

- [x] Remove the single 5 m snapping world point.
- [x] Cluster water observations into up to 4 independent shoreline regions.
- [x] Match and retain regions in world space; move anchors gradually along extended shorelines.
- [x] Use 4 bounded directional audio emitters with decorrelated phases.
- [x] Treat broad open sea as diffuse (no fabricated listener-following shore positions).
- [x] Keep the vanilla original as a low diffuse bed while regions are available.
- [x] Fade region arrivals and retirements independently, and restore vanilla when region observations cease.
- [x] Do not block normal updates waiting for ocean worker raycasts.
- [x] Restore source spatialBlend, original level handling and native resources on disable/shutdown.

## Transition cleanup and compatibility

- [x] Remove the special 0.25 s ambient-clip crossfade triggered by indoor/outdoor geometry changes.
- [x] Keep existing per-object SFX, HRTF, occlusion, room reverb and sound-following features intact.
- [x] Maintain random start phase for independent positional loops.
- [x] Preserve version 0.6.0 and record changes under Unreleased.

## Revision after window listening feedback (2026-11-21)

- The per-window hand-calculated distance, facing-angle, binary transmission and total-amplitude cap produced artificial steps and incorrect loudness.
- Directional window ambience now uses regular 3D AudioSources with logarithmic rolloff (near 2 m, far 36 m).
- Estimated aperture radius is captured when the source is first identified, so ray counts cannot pump its gain while moving.
- Multiple and bigger openings are allowed to increase total audible loudness.
- Removed opening-only obstruction ray jobs; physical occlusion of a window by a new interior wall is not yet modeled with the ordinary SFX occlusion pipeline. This is an explicit compromise to avoid repeated clipping and on/off decisions.
- The game-world audio bed is preserved. Ocean changes are unchanged by this revision.

## Validation gates

- [x] Compile Release build: 0 errors and 0 warnings.
- [x] Verify 11 source/design invariants and no new MonoBehaviour Update, LateUpdate or FixedUpdate hooks.
- [x] Install the DLL into Enngard and the package; verify both SHA256 hashes match the Release build.
- [ ] Verify Unity/Harmony patch registration after launching Valheim.
- [ ] Listen to rain while moving continuously toward/away from and sideways along a window.
- [ ] Test window visibility near an interior wall, a closing door and multiple nearby windows.
- [ ] Verify constant room and shoreline sound while standing still through repeated scans.
- [ ] Compare moving along shoreline, curved coast, island and opposite shorelines.
- [ ] Check ambient audio/voice/portal sounds and sconce phase after world reload and mod toggle.
- [ ] Compare recorded main-thread, audio-thread and real-voice budgets with previous 0.6.0 build. Bounded source budget: 8 openings x 2 loops, 4 shoreline loops plus the vanilla diffuse loop.
- [ ] Confirm no audible clicks during source starts/stops, world changes or clip transitions.

## Constraints / limitations

The 64-direction room sensor remains sparse. Source persistence and direct path checks reduce false source loss, but this is not a physical window polygon reconstruction. The ocean is approximated by up to 4 region emitters, not complete shoreline geometry. No claim of measured sonic or performance improvement is made until actual in-world A/B testing. Geometry sampling is asynchronous; only listener-distance and direction gain are recalculated immediately.

## Restore

Restore `backup/ENV_SOURCE_REDESIGN_20261121/src/*.cs` to `src`, the saved package files to `package`, and `installed_before.dll` to the Enngard `Wire-WiresAcoustics` plugin DLL with Valheim closed; then rebuild if needed.
