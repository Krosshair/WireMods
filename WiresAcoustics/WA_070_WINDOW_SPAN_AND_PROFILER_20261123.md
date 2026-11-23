# WA 0.7.0: narrow-window detection and profiler audit
Date: 2026-11-23 | RC only
Original run BepInEx LogOutput.log latest 18:48:49
Two WiresProfiler captures:
- 18:47:11: 5s /221 frames /22.75ms frame avg /44.0fps /130.2ms worst; WA 0.384ms/frame, AudioMan.Update patch 0.360ms/frame; WS 0.858ms/frame
- 18:47:51: 5s /293 frames /17.16ms avg /58.3fps /45.0ms worst; WA 0.271ms/frame, AudioMan.Update patch 0.244ms/frame; WS 1.451ms/frame
- WS internal native RenderToCubemap 0.176 and 2.954 ms/frame respectively (offset WS timing windows); no Unity GPU timing
- No Wire's Acoustics exception in latest BepInEx log.
- WiresProfiler reported 2367 method hooks and 5 failed unrelated coroutine hooks in each capture.
Investigation: Room's 64 Fibonacci sphere directions can classify a legitimate lone opening as a seam when all neighbors hit walls. Narrow windows then flip between zero direct and direct incident field with small listener movement. Existing prefab open fractions are ALREADY combined into Solids.First/Cut through Occlusion.Loss; no extra panel energy added.
Patch: Room uses already cast offset ray pair to verify the center escaped direction is wide enough to be physical opening. Offsets with no first hit or hits farther than surrounding wall unseal that candidate; truly isolated center-only seam stays sealed. No additional physics work, sources, per-frame hooks.
Static tests PASS: TestRainDoorway, TestRoofVsFloor, TestContinuousRainLayers, VerifyReleaseCandidate. net472 build 0 errors/warnings.
RC ZIP: C:\Users\kajet\WireMods\WiresAcoustics\staging\WiresAcoustics-0.7.0-RC-window-span-20261123.zip
DLL SHA256: 6A90A7B066DD8E366CA5660147AC79D3893417F176175707B39D92A7AFFC1626
Installed: INSTALLED IN ENNGARD, SHA VERIFIED
Rollback when Valheim closed: C:\Users\kajet\WireMods\WiresAcoustics\backup\WINDOW_TRIPLET_20261123\installed.dll
In-game listening/profile for revised candidate: PENDING. Fixed ray directions may still miss windows with zero sampled view; that requires separate redesign if audible problems persist.