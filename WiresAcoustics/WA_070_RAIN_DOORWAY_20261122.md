# WA 0.7.0 — Rain and ambience opening routing correction
Date: 2026-11-22
Status: Installed in Enngard, UNPUBLISHED release candidate. Requires actual in-game listening and CPU verification.

## Evidence
User screenshot (image(20261122-213041).png) shows an interior directly adjacent to large open double doors with visible rain beyond; exterior rain should remain substantially audible.
Most recent Enngard BepInEx and Unity Player logs were read: rainy weather preset Rain active in Meadows/Plains around 22:26-22:30. No WA exceptions, only unrelated Wire's Profiler coroutine hook warnings. Detailed WA room/portal trace is missing because Debug log=false, so this DOES NOT prove which portal was recognized at the specific screenshot.
Valheim source AudioMan.UpdateAmbientLoop switches m_ambientLoopSource.clip when its fade completes, independent of the room listener moving. The actual Rain EnvSetup supplies the rain ambient recording as m_ambientLoop. WA previously cached ambient copies until Room.Measure recalculated geometry. But Room skips geometry rays indefinitely for a stationary listener with no world changes. Result: old ambience clip persisted at each directional door/window copy across weather changes, and rain could remain heavily blocked by the roof/wall bed even with a large open door.

## Corrections
1. Existing Ambience.Glide is already called from Valheim's audio update. Check identity of the CURRENT audible ambient clip, only when a valid Room survey exists and volume is nonzero. Rebind sources using the CURRENT cached Portals.List and true room enclosure on the first actual clip change; NO geometry raycasts, new loops, coroutines, polling tasks or new update hooks. Correct audio sample phase is copied through existing Play.
2. In Ambience.Update, rank at most four verified room aperture candidates by their *perceived nearby opening strength* (solid-angle share and measured physical radius at the listener) instead of simply consuming the first two ranked by total ray count. Continue using only TWO existing directional copy slots and grace-matched portal positions. One distant roof/window cannot displace an adjacent wide double door merely because of its raw ray count.
3. Use WeatherAcoustics.OpeningGain(share,radius,distance) to give the real outdoor recording a stronger unobstructed path through a nearby large aperture, up to 1.0 of authored outdoor level (never additional invented energy in each copy). The existing rain-specific 250ms shelter transition and opening 250ms continuous fade are independent from the slower 1s generic bed.
4. The same recording plays as original 2D ambient bed and directional doorway clones. Crossfade diffuse amplitude against measured sum of active directional opening energies, instead of stacking full 2D rain and full 3D doorway rain together. No new AudioSources.
5. Detailed Debug log summary now includes current rain blend, rain bed gain/cutoff, strongest opening strength and routed opening mix to help diagnose specific outdoor openings if a follow-up listening test fails.
6. Weather identification remains the actual wet EnvSetup+active playing AudioClip match; unrelated wet ambience is not assumed to be rain. Original game's authored recording and volume remain source of truth. Wall/roof local muffle outside doorway unchanged.
7. The rest of WA (weapon shapes, analytic diffraction, wind/refraction, room acoustics) and WS untouched.

## Verification
- dotnet net472 Release 0 errors / 0 warnings.
- dev/WeatherAcousticsTests compiled production WeatherAcoustics, 2226 deterministic source-linked conditions, including monotonic near-door gain and never > vanilla source amplitude per opening.
- dev/TestRainDoorway.ps1 validates real source rebind on clip identity independent of room cast, cache-guided ranking, 250ms rain fade, energy-sharing, and no new physics; PASS.
- dev/TestAmbientWindTexture.ps1 updated for the same existing diffuse-bed branch, PASS.
- Full VerifyReleaseCandidate.ps1 nested suite 51 production C# files, all regression tests PASSED.
- Production DLL sha256: 23B904A8F789FDF99A59FC98BB9B4AA4F5F7B139A0382C577A95CA2E431670B2; installed Enngard + package.
- Staged ZIP: staging/WiresAcoustics-0.7.0-RC-doorway-rain-20261122.zip, sha256 EE85DC0C42F4A06F564C07D6B2BA3F858895955C1DE185138568C5FC9CA9D685, five required root entries verified.
- NOT VERIFIED: actual audible rain near this doorway and generic ambience in live game, CPU/GPU profiler, no intermittent missed portal detection. Existing limited 64-ray room geometry can still miss an aperture in atypical angles.

## Recovery
Backups of source, tests, package, original Enngard DLL at backup/WA_RAIN_DOORWAY_20261122. Restore.ps1 can roll back while Valheim is closed. No public release or tag created.
