# WA 0.7.0 — General Environmental Emitter Field, first provider: rain
Date: 2026-11-23 | Status: UNPUBLISHED release candidate, gameplay testing OPEN

## Why this rewrite exists
All previous rain-only models (listener sky visibility; window-radius volume; discrete directional rain patch copies; separately normalized roof trickle; 104 rain-only ear-to-ground rays; 2D weather bridge) were structurally wrong. The ambient recording should represent a broad set of real world emitting surfaces, not an imaginary source at windows or a fake point.

## Actual implementation
- New src/EnvironmentalField.cs (general EmitterDomain enum Surface/Point/Volume, EmitterSurface Terrain/Roof/Water/Vegetation, Patch {position,represented area,surface,domain}). At this time only the ExposedSurface provider for weather rain is hooked to gameplay. Ocean, vegetation, atmospheric wind and wildlife need separate future providers, not a universal terrain proxy.
- Candidate world surfaces: 8 grounded terrain points on a stable 2m world grid at 5.5m, 4 farther points at 17m, up to 2 extra outdoor points beyond actual Portals, up to 2 roof collider patches observed by the normal room survey. Actual heights via Heightmap.GetHeight, actual roof colliders via Room.Solids with prefab roof name classification. Ceiling floors do not become roof emitters.
- Source-side exposure: upward RaycastCommand from each surface. Tree foliage is allowed to transmit rain; obstructing buildings, overhangs, terrain block impact surfaces. This is not listener-sky visibility.
- Receiver path: each emitting patch is checked through the exact Batch.DirectWalls three-ray material/occlusion solver, and through at most two independent portal paths, each with separately traced listener-to-opening and opening-to-patch legs. Batch.PortalTransfer is now shared by SFX and environment, preserving the SAME SpectralPaths.Cascade, Diffraction.Opening, Portals.LengthGain, ThreeRayTransmission.IndirectAmplitude; the ordinary SFX path calls the factored helper too. This first version does not have SFX's per-emitter cached multi-corner probes or analytic one-wall area model; do not claim perfect path parity.
- Receiver aggregate: patch weight = represented surface area / (25 + squared physical distance). Source surface baseline includes sheltered patches in the denominator (a small hole cannot turn the field up to outdoors). SpectralPaths.HighEnergy and Decode combine low/high energies separately, clamp to the original outdoor ambient reference, and output ONE filter transfer. No individual Unity AudioSources exist for field patches.
- Audio integration: Ambience.Glide uses only the existing AudioMan ambient loop. While that clip is the active rainy environment, its AcousticFilter is set to EnvironmentalField.Gain/Cutoff. No extra voice, bridge, output AudioSource relocation or bespoke roof/cave gain constant. Valheim controls clip start/fade. Other ambience and wind remain on prior known routing until specific field providers are implemented.
- Scheduling: at most once per existing Room 200ms tick, only when wet weather active, listener moves >=0.45m or geometry/door generation changes. Single independent async raycast job with 16 hits/ray, at most 16 surface patches * (1 upward +3 direct +2 *6 portal) = 256 commands. No new Unity per-frame hooks/coroutines; persistent native arrays reused. Previous 104 rainy physics rays were removed from Room job. Weather activation while stationary causes a new survey. Jobs completed/disposed on world reset and mod disable.
- Debug: EnvironmentalField.Summary prints source patch count, valid exposed surface count, portal path count, resulting acoustic gain/low-pass and number of submitted jobs.

## Validation performed
- Production net472 Release: zero errors/warnings.
- Full dev/VerifyReleaseCandidate.ps1 across 52 C# sources: PASS. Explicit no new per-frame/coroutine hooks check.
- Source-linked field material/portal integration tests PASS.
- ~6000 normalized surface energy numeric invariants PASS. Area representation linear, energy never > vanilla outdoors, blocked surfaces not silently removed from baseline denominator.
- 7019 authored roof vs floor + ordinary biome/wind aperture geometry tests PASS after deleting old unused rain-only equations.
- Existing SFX/weapon/source-extent/Fresnel/HRTF/wind/ownership regressions PASS.
- In-game sound and CPU/performance are NOT TESTED in this coding session. Native scheduled 256-ray field jobs may have nontrivial CPU cost; compare controlled scenes, including basements and dense bases. Potential missing rays at very narrow apertures or outside terrain outside loaded Heightmap range remain OPEN.

## Gameplay test protocol
1. Run the same open-arch room in active rain. One recording should be audible from exposed outside surface patches through real openings without proximity hacks.
2. Move into a sealed basement and compare with attic: outer roof surfaces exposed and blocked paths through floors become naturally different.
3. Open/close doors and windows while standing; geometry invalidation refreshes both routes.
4. Walk across window boundaries at differing angles; an absent single ray should not destroy the entire field, but sparse geometry remains a limitation.
5. Capture Wire's Profiler via keypad9: WA cost, when moving/raining versus still, and WS render effects. Performance regression is a blocker for release.
6. Test weather selection changes while standing still; no duplicated voices.

## Rollback and release state
- Full backup: backup/ENVIRONMENT_FIELDS_20261123/{src,dev,package,installed.dll}.
- Rollback script: backup/ENVIRONMENT_FIELDS_20261123/Restore.ps1 (Valheim must be closed).
- Installed and SHA256-verified in Enngard, package, and staged ZIP: production DLL 29F409C3A0B4DD5C91A2B985370BA342C3079DF461AAF235CF0FE18BCA1D05B2.
- Staged zip: staging/WiresAcoustics-0.7.0-RC-environmental-field-20261123.zip, SHA256 708DF7B2176BBACE1F711B24BFF262175CB07CBD6CCA411B78BFE1FDF8EC560F, 215547 bytes. Archive DLL checksum verified byte-for-byte; manifest and five root entries verified.
- 7019 environment/surface classification numerical tests, ~6000 field energy invariants, and all release-candidate regressions passed. No runtime audio/profiler claims.
- WS and the profiler untouched. No public release or tag.
