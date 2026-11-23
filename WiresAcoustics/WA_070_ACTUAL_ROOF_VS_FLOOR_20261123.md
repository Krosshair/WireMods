# WA 0.7.0: Actual Exposed Roof versus Intermediate Floor
2026-11-23 | Unpublished release candidate | In-game listening and performance unverified

## User feedback
Basement sounds like attic: the same bright, close roof rain trickling overhead.
Root cause in the production src/Room.cs: each existing 64-direction room survey counted ANY built overhead collider as BuiltRoof. Ordinary intermediate floors are built too. Room.BuiltRoof then fed WeatherAcoustics.RoofRainPresence, which lifted the one original ambient rain clip towards 0.72 gain and 6500 Hz roof emphasis even for listeners in basements.

## Correction
- In Solids.Make (cached collider classification), store Roof for a recognized authored Valheim roof prefab. The helper WeatherAcoustics.IsRoofPieceName checks roof-prefab naming case-insensitively and excludes wall_roof gable walls. Wood/stone intermediate floors are never roofs.
- On existing upward room rays (y > 0.35), only the FIRST hit on such a roof contributes to actual BuiltRoof. A floor immediately above the basement intercepts the ray and blocks the roof-trickling boost. The floor still counts normally toward room enclosure, absorption and acoustic wall geometry.
- Roof contribution is multiplied by true covered fraction (1-OpenFraction) and a continuous inverse-quadratic height weighting: 1/(1+(vertical distance/4.5m)^2), based on the EXISTING measured ray distance projected vertically. Upper roofs farther away are less pronounced.
- The indirect diffuse rain floor, ordinary exterior exposure through windows, weather clip, single AudioSource, wind/biome ambience, room reverb and other WA features remain unchanged.
- No extra raycasts, no new audio sources, no frame callbacks/coroutines, no new physics or hot-path string queries (classification cached per collider).
- Recognized roofs include wood_roof, darkwood_roof, piece_grausten_roof etc, verified against original exported Valheim piece prefab names. Unsupported modded roofs without 'roof' in prefab name will conservatively use indirect rain only. A flat exposed deck made from a floor prefab is not treated as a roof, since a first-hit ray alone cannot prove it is the external surface.

## Tests
- net472 production build 0 errors, 0 warnings.
- 5,239 WeatherAcousticsTests cases, including monotonic 0–15m roof-depth weight, authored roof / floor / gable classification, basement vs attic gain.
- dev/TestRoofVsFloor.ps1 checks actual Valheim exported roof/floor prefabs and integration with the cached collider, existing room rays, single-loop rain mixer and zero additional raycasts.
- Full dev/VerifyReleaseCandidate.ps1 incl. nested implementation suite PASS across all 52 source files. Source digest SHA256 6F1EAED3A5C29967E10C25DA8ADB22704E1DC4E9D072D4392332C944B46EEFFA.
- Actual in-game basement and attic listening and audio profilers still pending.
- DLL SHA256 6F1EAED3A5C29967E10C25DA8ADB22704E1DC4E9D072D4392332C944B46EEFFA installed into Enngard and package.
- Staged ZIP staging/WiresAcoustics-0.7.0-RC-actual-roof-rain-20261123.zip, SHA256 D2F71875AB2CCFBB0A66B63BE60DC6993C1FC1C9DB11212A3871EDDDF3593C88, five verified root entries. Not published.

## Rollback
backup/RAIN_ACTUAL_ROOF_20261123/Restore.ps1 (run only when Valheim is closed), using saved pre-change source, relevant tests, package and original installed DLL.
No public release. WS and profiler unchanged.
