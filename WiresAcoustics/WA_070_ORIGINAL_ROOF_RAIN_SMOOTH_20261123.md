# WA 0.7.0 - Original roof-rain coloration restored with smoothing
2026-11-23 | UNPUBLISHED local release candidate

## User feedback
- Zero-extra-ray incoming exterior field made overall rain sound good.
- User specifically misses earlier roof treatment that worked, but occasionally switched on/off when sparse room survey missed the roof.
- User explicitly rejected fabricated/generated wood/stone/metal rain audio. No invented audio in this build.

## Changes (minimal)
- Restored old WeatherAcoustics.RoofRainPresence and roof-only blend constants from November 23 RAIN_OPEN_ARCH backup: reference rain gain .72 and roof brightness low-pass 6500 Hz.
- Restored as an overlay on top of the **new working EnvironmentalField**. It only raises gain/brightness toward the old roof reference when necessary. It never reduces gain from real open arches and never adds the old global indirect-rain floor. No separate clip, no new AudioSource, no synthesized recording, no bridge.
- Roof identification remains existing Room first-hit roof collider classification. Distance weighting RoofImpactWeight still used, floors between storeys, roof-shaped gable wall panels ignored. Basement therefore should not receive immediate roof character from a floor.
- Ambience Glide is the existing AudioMan update. It uses independent exponential smoothing on roof presence with attack time constant 0.42 s, release 1.25 s, then the existing 0.38s general field smoothing and audio-thread smoothing. Intermittent missing rays should cause small dips instead of abrupt cut-outs. Reset clears roof presence.
- Debug Ambience Summary now reports smoothly blended roof rain presence.
- Main EnvironmentalField (64 directions/192 existing room rays), wind, ocean, wildlife and ordinary SFX unchanged.
- No new per-frame update hooks, coroutines, PCM sound generation, native arrays or physics jobs.

## Verified
- Production net472 build: 0 warnings and 0 errors.
- Source-linked WeatherAcousticsTests: 11,463 numerical checks (old roof presence, mix gain/brightness, monotonic coverage, one missed 200 ms survey response, release fade, existing roof/gable/floor classification).
- TestRoofVsFloor and all WA regression checks PASS, full VerifyReleaseCandidate.ps1, 52 production C# source files. Report RELEASE_CANDIDATE_VERIFICATION_20261122.txt is refreshed with current SHA.
- No actual live-game listening/performance check in this coding session.

## In-game acceptance
- Attic below original real roof: existing rain loop subtly louder/brighter.
- Basement directly below an intermediate floor: no immediate direct rooftop boost.
- During movement or tiny camera/listener changes, the roof effect must no longer abruptly toggle.
- Wide arches should still retain previous exterior field volume/clarity.
- Profiling: no additional physics rays or voices; check normal WA CPU while rain active.
- If roof effect remains intermittent, inspect measured Room.BuiltRoof versus smooth 'roof rain mix' debug summary before changing ray geometry again.

## Rollback
backup/RAIN_ROOF_LEGACY_GLIDE_20261123/Restore.ps1. Game must be closed. WS untouched. Not published.

## Installed build
Enngard DLL SHA256: 41BB2AC28664679F00D5D84443CE89B206C45E9CB9FB748B29DB1DF1C3BF83AB
ZIP: C:\Users\kajet\WireMods\WiresAcoustics\staging\WiresAcoustics-0.7.0-RC-original-roof-smooth-20261123.zip
ZIP SHA256: EED42956D224F8299A7E4799FD67AA323B9FF7459D6D912007E36B6060A7F585
ZIP bytes: 216084
