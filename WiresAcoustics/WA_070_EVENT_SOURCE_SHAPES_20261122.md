# WA 0.7.0: Event-Accurate Source Extents
Date: 2026-11-22
Status: local release candidate, not published. Runtime listening and rain checks remain open.

## Cause
SourceVolume.Project gated cached owner geometry on AudioSource.loop and otherwise used a fabricated 30cm sphere. Playback duration is not evidence of an emitting object's physical extent.

## Changes
- Removed loop requirement for owned emission areas. Cached owner collider boxes, rotated and scaled, now apply to one-shot and looping sources alike.
- Added a LocalizedEvent flag to distinguish precise event spots from distributed sources. Known hand/weapon/hinge/machine-control placements and chest lids keep their actual world position. An off-pivot one-shot from an explicitly assigned owner also remains localized.
- Localized events no longer inherit HRTF apparent width from their parent object and do not receive a whole-owner visibility ray.
- Unshaped sources default to an event point. There is no invented 30cm fallback. An optional user-configurable nonzero radius is still supported for an otherwise unspecified emitting area.
- The analytic wall solver keeps a 1cm numerical radius for point emitters to avoid division by zero. Point emitters schedule zero EXTRA volume raycasts; original direct raycasting stays intact.
- Existing 4/8/16/32 source-volume maximum and event-driven geometry cache are unchanged. No new frame updates, coroutines or physics scans. Prior rain, wind, propagation features unchanged. WS untouched.
- Updated Enngard user config Unshaped source sphere radius from 0.3 to 0, backed up its original value.

## Verification
- Production build succeeded without warnings or errors.
- 1281 deterministic geometry checks, including box samples at multiple yaw angles and scales, nonloop/loop area parity, localized event with manual fallback radius, zero default sphere rays.
- 1604 analytic Fresnel continuity tests.
- dev/TestEventSourceExtent.ps1 static event and HRTF integration checks passed.
- Full VerifyReleaseCandidate.ps1 suite passed across 51 C# source files.
- Production DLL SHA256: 5C5438AE4A142253F76F9B2090CFF15E0AD13331F05F7FF4B522686E381171AF; installed in Enngard and package.
- ZIP staging/WiresAcoustics-0.7.0-RC-event-source-geometry-20261122.zip; SHA256 E5B182DCAD36A41A71CA4AC8C7E897ED70640D9A8C42FE6081A5651957B52949. Five required root entries verified. Not published.
- Not established by tests: in-game sound quality, individual event classifications, runtime overhead, live rain response.

## Recovery
backup/WA_EVENT_SHAPES_20261122 contains original relevant source, tests, package, installed Enngard DLL and user config.
backup/WA_EVENT_SHAPES_20261122/Restore.ps1 rolls back when Valheim is closed.
