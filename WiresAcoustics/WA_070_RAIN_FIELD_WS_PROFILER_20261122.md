# WA 0.7.0 rain field + WS profiler diagnostic
November 22, 2026 — unpublished; active gameplay and performance unverified

## User feedback
Enngard in rain, indoors beside large open doors: previous roof + two-opening rain copy model too muffled. Perceptual feedback: perhaps somewhat better, still incorrect. The requested approximation is distributed rain *impacts on exposed outdoor ground*, not two generic door emitters or literal simulated rain particles.

## WA implementation
- New src/RainField.cs, uses cached 64 ray directions from Room.Measure; no new raycasts, physics jobs, MonoBehaviour updates, polling tasks, or individual raindrops.
- Eight azimuth sectors model many conceptual outdoor impact points around the listener. Ray directions near the horizon that actually escape the room contribute to that sector's audible exposure; closed sectors don't.
- Up to four strongest exposed sectors are selected, using existing exposure fraction and deterministic positions on exterior ground approximately 8m away and 1.5m below ear. A few old voices can briefly overlap while fading for 250ms. This is a cheap acoustic proxy, not exact impact or terrain collision.
- Each channel plays the game's current rainy environmental recording, with deterministic sample offsets so copies are not phase-coherent; the same ambient mixer and authored volume are retained. Sources fade with a 250ms bounded envelope.
- Inside, directional outdoor rain-impact sources take over a corresponding share of the original 2D ambience, so audio isn't doubled. Rain falling onto roof stays in the already existing roof-impact bed. Outdoors, keep the unmodified diffuse original.
- Rain no longer relies on a pair of door-anchored copies; generic birds, other ambience, and wind still use the prior portal routing. The original wind and weapon visual geometry improvements remain.
- RainField.Reset called during existing Ambience.Reset, destroys children across worlds. AudioSources are bounded and reused. Default sound event point fallback unchanged.

## WS profiling
- WS already had its class/method hooking enabled in Enngard: Look inside mods = Wire's Shadows; Remove hooks after capture = true; WS Timing log = true. Latest prior capture confirms ShadowCache.DrawFaceCore in the expensive-method list.
- Refreshed WiresProfiler default to instrument WS and auto-remove hooks after each capture.
- In the report produced by Keypad9, captures also embed WS's existing 5-second internal timing lines appearing after profiler capture start. These separate native Camera.RenderToCubemap CPU wall time, worst case, face redraw counts, new map face count, partial/full updates and near-resolution promotion storms.
- Timing windows can be phase-offset, so do not add WS timing numbers into profiler self-time; GPU time is still not directly measured.
- No WiresShadows source, DLL or configuration changed.

## Verification
- WA 52 source production release full regression/static gate PASS, 0 build warnings/errors; deployed DLL SHA256 B0A605841C473C23F7F018FF1FA5F3ABEFCE554909EC0E612A4871BE67B96A97.
- WA staged ZIP: staging/WiresAcoustics-0.7.0-RC-distributed-rain-field-rain-pending.zip, SHA256 13BD7593A04D4411EFCA007C263E835A0F57CD13F68CC8F8EE0E2A324E8BB36F, 5 validated root entries.
- Rain-field math/source harness passes sector classification, open doorway synthetic view, no new rays, source relinking, no user-ground truth assumptions.
- WS profiler source builds 0 warnings/errors; integration test against actual current WS log passes. Installed profiler DLL SHA256 4A70A8A456F3FE23CA74C43017B3FE1DBF0616D12CD7E8BEA2E0F2F7BA0EBBB5; staged at WiresProfiler/staging/WiresProfiler-0.1.0-WS-timing-20261122.dll.
- In-game audibility and CPU performance NOT tested in this coding session; user must listen and run Keypad9 next time.

## Backups
- WA: WiresAcoustics/backup/WA_RAIN_FIELD_20261122/Restore.ps1, with source, tests, package and installed DLL.
- Profiler: WiresProfiler/backup/WS_DIAGNOSTIC_CAPTURE_20261122/Restore.ps1, with source, config and installed DLL.

## Launch/test
- Enngard: new WA rain source field and profiler instrumentation are installed. Existing WS DLL unchanged (last modified 2026-11-21 12:43:42).
- Reproduce same rainy open-door scene. Listen from open air, inside under roof beside open door, then with doors closed; note if directional exterior rain survives inside.
- Press Keypad9 near identical busy WS shadow scene for 5s; the report is saved at BepInEx/WiresProfiler/capture_YYYYMMDD_HHMMSS.txt, including WS internal timing. Useful control: repeat with a stationary camera after caches settle and during map rebuilds.
- Neither mod publicly published. WS untouched.
