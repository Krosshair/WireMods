# WiresAcoustics 0.7.0 - finite development acceptance gate
2026-11-22
Purpose: Stop indefinite patch/compare/test cycles. "Perfect" cannot be proved by repeating code audits. Declare goals and finite exit criteria. This is a DEVELOPMENT checklist, not an assertion of complete parity with Wwise/Steam Audio.

## Definition of done for the WA project, as opposed to reimplementing every feature of other engines
- Correctness of implemented models within their documented approximations.
- Static/automated testing for spectral transmission, direct/indirect sound, geometry events, graph construction, numerical continuity, state reset, deterministic and thread-safe debug meters.
- No newly introduced per-frame Update/LateUpdate/FixedUpdate, coroutines or polling.
- Computational budget measured within Valheim (requires a running game and profiler).
- Audible behavior in the actual target runtime with its native Unity audio mixer, geometry/door transitions, varied sources, virtual voices and HRTF. This final runtime acceptance cannot honestly be guaranteed by static code inspection.

## What constitutes an explicit scope addition (and is NOT silently implied by comparison)
- Native Steam Audio simulation scene + reflection/path baking or runtime tracer
- Wwise equivalent authored AkRoom/AkPortal topology graph from Valheim piece meshes
- SOFA HRTF renderer with dataset licensing, sample-rate convolution and headphone validation
- Real-time multiband diffraction around arbitrary sequences of edges
- Convolution IR reverb for procedurally changing world topology
These are not interchangeable small "fixes." They are separate engine-level subsystems, each with native integration, asset format and resource demands. The user should not be trapped in an endless "not done" cycle because WA is not a copy of both commercial engines.

## Explicit PASS/FAIL gates for present implementation
PASS: net472 Release compiles without warnings/errors.
PASS: currently developed Fresnel/DSP/underwater/diffraction/corner numerical model checks.
PASS: multithread diagnostic packet integrity tests.
PASS: geometry-cache and portal graph deterministic tests.
PASS: trace report and analysis generate parseable machine data.
OPEN: CPU budget profiled in Valheim for dense base AND outdoor baseline.
OPEN: gameplay audio transitions, especially wall placement, door closure, window/small aperture, partial building.
OPEN: HRTF subjective localization and final mixer behavior (no automatic proof from synthetic tests).
OPEN: any actual gameplay regression created by current in-development changes.
IMPORTANT: These four runtime checkpoints are final acceptance checks, not an invitation to reopen all engine features forever.

## This pass: actual portal evidence vs open sky
Problem: previous Portals.Find considered Room.Out(i) including rays to unobstructed sky, then grouped them into candidate openings if near any blocked rays. Outdoors can be incorrectly classified as windows. Also, a hard global Enclosure <0.35 early exit prevented identifying verified openings in partially built structures.

Change:
- Room.PortalEvidence(i) exposes only rays measured by the room scanner as gaps adjacent to constructed geometry, excluding seams SealedBy[].
- Portals.Find now uses Room.Out(i) && Room.PortalEvidence(i) for a candidate.
- Removed .35 global enclosure cutoff; locally verified constructed openings still count in partial bases.
- No extra raycasts, per-frame hooks, coroutines, per-source calculations, or loops.
- Caveat: with 64 rays small windows may still be missed; changing to real geometry representation would be separate scope.

Immediate verification:
- net472 build 0 errors/warnings.
- dev/PortalTopologyTests compiled actual Portals.cs with synthetic Room evidence, checked 40 masks and 600 index permutations, and independently tested low-enclosure supported opening vs unsupported open sky.
- Full VerifyEveryImplementation.ps1 regression suite passed.

## Established-system comparison
Wwise Room Portals are explicit geometry-associated connections, so an open outdoor direction is not implicitly a valid portal:
https://documentation.help/Wwise-Unity/pg__rooms__portals__tut.html
Steam Audio differentiates geometry occlusion/transmission from traced/baked acoustic paths and dynamic geometry updates:
https://valvesoftware.github.io/steam-audio/doc/unity/guide.html
WA's detector is a finite approximation to the geometric evidence requirement, not equivalent to those engines.

## Rollback
Close Valheim; run backup\PORTAL_EVIDENCE_20261122\Restore.ps1
