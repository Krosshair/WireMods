# WA 0.7.0: Open-arch rain transmission fix — November 23, 2026

## Feedback / reproduced logic
At the user-supplied screenshot position (large open arches left/right of player under a floor/roof), rain was still highly muffled. Code audit identified two objectively inverted/unstable transmission conditions:
1. RainField.Access used broad-horizon Room.Out ray coverage ONLY if NO finite Portals had ever been cached. Once a small narrow window was cached, the much larger open arches were discarded from rain visibility.
2. Ambience.Glide multiplied the direct opening access by clamp(max(s_insideTarget*2, Room.BuiltRoof*2)). A very open hall has reduced enclosure by definition; after a floor-vs-roof fix its first overhead surface may NOT be an actual exterior roof. This multiplier therefore virtually erased direct rain exactly where the exterior exposure was greatest.

## Implementation
- RainField.Access always takes the max of measured current horizon exposure and continuously distance-based cached opening geometry, then applies the existing normalized final 2x open-path perceptual scale. No double gain for two independent measurements describing the same outside path.
- Ambience.Glide no longer attenuates direct window/open-arch rain path by general enclosure or identified roof fraction.
- Indoor indirect rain floor, attic/basement rooftop sound distinction, the original single Valheim rain AudioSource and its existing AcousticFilter, weather clip handling, other ambience/wind, reverb and WS all unchanged.
- Enhanced RainField.Summary debug diagnostics to report BOTH measured open-horizon and final combined single-loop gain; useful when checking geometry in a specific new base.
- No extra RaycastCommands, new native jobs, audio source copies, independent Updates, coroutines, or extra scene traversal.

## Verification
- Production net472 build zero warnings/errors; full VerifyReleaseCandidate.ps1 with all 52 production C# files passed.
- dev/TestRainImpactField.ps1 source-extracted math cases now include large-horizon-plus-small-cached-window, window-plus-small-ray-count, non-additive equal evidence, zero and full exposure, and monotonic horizon progression. Source-linked integration asserts both paths and absence of obsolete enclosure multiplication.
- dev/TestContinuousRainLayers.ps1 asserts original Valheim ambient AudioSource still sole rain source.
- DLL SHA256 B1696EAF0FB1A5BFAFCFFF9611E056CF8C10250A5991E60CD22D0F6550AA3AB1, installed into Enngard and package, verified.
- Staged ZIP staging/WiresAcoustics-0.7.0-RC-open-arch-rain-20261123.zip, SHA256 3B9068272DFC34D8DE14C0B52BD2BE3AA9C53AD46737BC9BA72206DBBCBFECDB, five validated root entries.
- In-game audio and native performance NOT YET VERIFIED, no public release.
- Backup/rollback at backup/RAIN_OPEN_ARCH_20261123/Restore.ps1 (game must be closed).
