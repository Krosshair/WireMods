# WA 0.7.0 — listener visibility to outdoor ground
2026-11-23 | Unpublished release candidate | Subjective gameplay and performance still pending

## Problem
In a wide-open covered base with arches, the character visibly has outside lines of sight but the original Valheim rain stays nearly inaudible. Previous WA models used 64 sparse room rays looking for empty sky or inferred windows. These can miss openings, fail near fences and rocks, or misclassify outdoor ground as a hit. The room’s physical enclosure should not veto audible outdoor rain.

## New visibility model
- src/RainField.cs adds 52 candidate outdoor terrain positions from the character's actual listening/ear position, independent of camera forward: 24 azimuth directions at 4.5m and 10m radius, staggered by half a step (combined 7.5 degree directions), plus four positions aimed through existing detected door/window portals.
- Actual terrain Y comes from Valheim's cached Heightmap.GetHeight/GetWorldHeight, not ZoneSystem.GetGroundHeight (which performs a costly synchronous downward Physics.Raycast). No new native allocations in the room job.
- For each valid target, two RaycastCommands are appended to the existing async Room job: a line from ears to *open air above the terrain*, and an upward 20m shelter test at that air point. Ground proximity for acoustic gain remains the actual terrain position, avoiding requiring eye-to-ground LOS through an elevated window sill or upper-floor edge.
- A candidate is directly audible if the line is clear and no constructed roof, floor, stone overhang or other structural shelter lies above it. A tree canopy is not treated as a solid building roof.
- A single clear outdoor ground region gives physically motivated, distance-dependent direct rain access (near 5m ~0.9, 10m ~0.67, farther progressively lower), combined by maximum with the existing window and broad-horizon evidence. No double-counted volume from duplicate rays.
- Access drives gain and low-pass of ONLY Valheim's original single rain recording. No extra AudioSources, no bridge, no per-frame physics hooks.
- Adds precisely 104 RaycastCommands during rainy room resurvey (old 192 => 296 commands at most); dry surveys retain 192. Runs at most at the existing room measurement cadence (~5Hz) and only when the listener moves >0.18m, geometry changes, or rain is newly selected. One batched async physics job, no main-thread Raycast from ZoneSystem and no additional coroutines.
- Debug (when enabled) now includes visible outdoor-ground sample count and nearest actual terrain distance in RainField.Summary alongside existing cached window and horizon information. If a direct path remains unrecognized, these numbers isolate actual sampling misses.

## Scope and limitations
- This is sampled 360-degree ground visibility, not an exhaustive proof of every arbitrarily narrow/slanted window. A narrow aperture can still fall between 7.5-degree staggered sample angles, especially at far range. Four portal-targeted samples improve known openings.
- Only terrain impact proximity is modeled. Rain on an elevated outdoor deck may be underrepresented when the underlying terrain is sheltered. A tree canopy is not a solid roof.
- Native ray cost plus 52 cached Heightmap reads per rainy *room survey* require real-game profiling. Without a gameplay run we cannot confirm the sound or frame cost.
- Existing one-source rain, attic vs basement, general ambience, weapon source geometry and WS code untouched.

## Test result
- Production net472 build: 0 warnings, 0 errors.
- dev/TestRainImpactField.ps1 source-extracted math tested continuous GroundAccess against 5cm increments, full-angle geometry, many open/closed cases.
- dev/TestRainGroundSight.ps1 checked exact source paths, actual Heightmap and ZoneSystem source distinction, 104 bounded wet-only batched commands, air LOS + sky roof rejection, and use of the original rain filter only.
- Full dev/VerifyReleaseCandidate.ps1 PASS on 52 production sources; report RELEASE_CANDIDATE_VERIFICATION_20261122.txt, binary SHA256 244864ED8024B45FA0FE683E8E3518D38A62367F141ED630F6FAF21FBBB7A50B.
- Staged ZIP staging/WiresAcoustics-0.7.0-RC-ground-line-of-sight-20261123.zip, SHA256 A2C30CEC74CD77410C4F7D262ED57E29EA88DB4E847D4C0DDDCC0A09BF073DAD. Five required entries verified.
- Production DLL SHA256 244864ED8024B45FA0FE683E8E3518D38A62367F141ED630F6FAF21FBBB7A50B, installed and byte-verified against Enngard and package.
- In-game audition and performance/profiler gates OPEN, not published.

## Rollback
backup/RAIN_GROUND_SIGHT_20261123/Restore.ps1, with Valheim closed.
