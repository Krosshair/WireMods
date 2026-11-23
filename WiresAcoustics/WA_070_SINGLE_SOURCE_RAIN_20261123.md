# WA 0.7.0 — Single-source rain filter
2026-11-23 | UNPUBLISHED release candidate

## User decision
The former three-layer rain playback sounded inconsistent: original game ambient clip, up to 4 directional phase-offset copies, and an extra 2D weather-transition bridge. Explicit instruction: remove multiple rain copies, just filter one.

## Implementation
- Only the existing Valheim AudioMan.m_ambientLoopSource plays the rain recording.
- Removed all AudioSource/GameObject construction and playback from src/RainField.cs; the class is now a pure listener-to-window geometry helper, retaining the cached 64-direction room survey, physical aperture positions, doorway/world invalidation, and continuous WindowAccess(distance,radius) calculations.
- Ambience.Glide calls RainField.Access(Hearing.At) to determine how much of the existing one rain loop is heard directly through an opening; gain blends from the previously verified structural rain floor/roof response toward 1 outdoors, and frequency cutoff blends from measured wall/roof response toward 22000 Hz near an open window.
- Actual acoustic filtering remains in AcousticFilter on the ORIGINAL AudioSource; no separate direct or indirect audio voices. Exposure does not mute the original recording to trade it against a new source. Other weather/wind/biome ambient portal logic preserved.
- Tracked previously played rain clip explicitly to ensure the legacy generic ambience portal copies do NOT spawn a duplicate of rainfall during fade-out. World reset clears this identity.
- Removed WeatherAcoustics.BridgeNeeded, including the temporary bridge creation, volume compensation, separate phases and manual sound lifecycle. Valheim's own two-second fade-out/clip-switch/fade-in remains active. It may take time for the new rain clip to start, but it uses only one physical AudioSource as explicitly requested.
- No changes to Wire's Shadows or WiresProfiler, no new callbacks, no new raycasts, no extra audio sources or synthetic events, no per-frame polling outside already-running AudioMan.Update.
- Simplified package README and CHANGELOG, removing outdated rain-source prototypes from current-release notes.

## Verification
- Production net472 Release build 0 errors / 0 warnings.
- dev/TestRainImpactField.ps1 tests source-extracted WindowAccess, ray direction, smooth 5cm step distance and strictly no generated rain playback.
- dev/TestContinuousRainLayers.ps1 asserts original AudioSource, one filtered gain/cutoff path, no bridge, no clones.
- dev/TestRainDoorway.ps1 asserts rain-copy suppression and window opening cache, while ordinary wind/biome ambience portal routing remains.
- Source-linked WeatherAcoustics numerical tests for roof response and 250ms filter transition pass.
- Full dev/VerifyReleaseCandidate.ps1 static + nested implementation gate across 52 C# files PASSED.
- In-game listening and profiler performance NOT YET VERIFIED.

## Rollback
backup/RAIN_SINGLE_SOURCE_20261123/Restore.ps1; Valheim MUST be closed.
Previous installed Enngard WA DLL backed up at backup/RAIN_SINGLE_SOURCE_20261123/installed.dll.

## Next in-game test
1. Stay in established rain, stand outside, walk up to a large open window, then retreat into the room. Expect one continuous recording changing gain and low-pass rather than audio copies starting/stopping.
2. Close/open door and ensure cached exposure eventually changes with geometry.
3. Trigger a clear-to-rain weather transition while stationary. The original Valheim 2-second ambient fade is now unmodified, so initial weather-onset delay is expected and can be addressed separately by changing the original fade timing WITHOUT adding any AudioSource.
4. Compare WA profiler to baseline. No extra rain audio voices remain.
