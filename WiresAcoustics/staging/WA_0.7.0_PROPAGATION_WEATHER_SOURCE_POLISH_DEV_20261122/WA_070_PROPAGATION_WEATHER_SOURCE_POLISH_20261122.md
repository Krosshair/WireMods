# Wire's Acoustics 0.7.0 – propagation, weather and source-location polish
Private developer candidate, 22 November 2026. Not a public release.

## Purpose
This is a substantive cleanup and audio-behavior revision, not a
one-off muffling hotfix. It addresses Valheim-specific complaints:
outdoor rain disappearing indoors, unconvincing clear/muffled switching,
sound effects sourced from incorrect objects, and avoidable processing.

## Implemented in production
1. src\AnalyticOcclusion.cs:
   Detected single BoxCollider walls use exact planar disk/rectangle
   overlap plus published single knife-edge Fresnel complex-field
   intensity, evaluated at low 125Hz and mid 2kHz against Valheim's
   existing authored wall transmission. Near edge, diffraction lets
   most of the sound through; deep in a solid screen shadow it
   attenuates gradually rather than abruptly switching to a full
   wall state. Other multiwall, terrain, and portal cases still
   use existing bounded geometry path solver.
   The closest validated box edge informs perceived direction.
   There are no new frame hooks or raycasts. Existing tracked-source
   updates follow listener/source movement continuously.
   A pose/strength/water/indirect-energy cache avoids expensive
   Fresnel work for completely stationary unchanged sources.
   For a cached, unchanged analytical wall, subsequent movement
   rechecks omit redundant 4-32 volume rays, retaining the three
   direct discovery rays. New geometry restores the volume sample.
   Independently measured opening routes remain enabled even
   while redundant same-wall corner probes stay suppressed.

2. src\Batch.cs, Starts.cs, Filter.cs:
   Analytical wall paths no longer add a second independent sampled
   corner path for the same wall. Duplicate corner probes are not
   scheduled for cached analytical walls; start and continuing
   sounds get consistent Fresnel gain and initial perceived direction.
   For independent portal paths existing indirect energy remains
   separate rather than being guessed away. World changes still
   request immediate filter response; normal motion uses glide.

3. src\WeatherAcoustics.cs, Ambience.cs, Room.cs:
   Rain identification uses ValheimSOURCE's active EnvSetup.m_ambientLoop
   and actual AudioMan.m_ambientLoopSource.clip together with its wet flag.
   Wet ground or a mismatched ambient clip is not automatically "rain."
   Built roofing contributes to the continued audibility of Valheim's
   real rain recording while indoors. Partial roof/wall screens count
   proportionally to their static prefab open-area properties.
   No procedural weather clips, duplicate rain emitters or new physics
   rays. Outdoor copies remain located at existing measured portals.
   Roof-impact gain (.72) / high-frequency texture (6500 Hz) are
   explicit Valheim perceptual tuning properties, not empirical laws.

4. src\Spots.cs:
   Sound effects with no existing owner, spawned precisely at piece
   pivots, may be attributed to a nearby building only if EXACTLY one
   matching object owner exists. Physics collider order cannot
   arbitrarily switch sound source to a different nearby machine.
   A reusable 64-collider event-time buffer handles denser bases.
   If buffer overflows, source stays where the original prefab
   played it rather than being attached to a random owner.
   Existing known weapon/head/chest/fire/door/switch owner paths remain.

## Direct code and reference evidence
- ValheimSOURCE\csharp\assembly_valheim\EnvMan.cs:
  EnvSetup.m_ambientLoop is selected separately from wetness.
- ValheimSOURCE\csharp\assembly_valheim\AudioMan.cs:
  m_ambientLoopSource.clip is assigned during vanilla ambient transition.
- Existing WA FresnelEdge.cs includes the tested Kirchhoff half-plane
  table and documented single-edge ITU-R P.526 parameterization.
- No runtime prefab mesh traversal. Open-panel values remain fixed
  authored Valheim properties.

## Tests and QA
Numerical test harness compiles actual production acoustic source:
dev\AnalyticOcclusionTests (7844+ assertions; update actual count)
- full/half/quarter/disk/tangent and 1200-step motion continuity
- rotated/slender bars, disabled source strength and invalidation
- deep/grazing/open sound-gain ordering, smooth direction to edge
Synthetic source trajectory at unit test material: deep 0.440,
grazing 0.837, open 1.000 (these are NOT ingame measurements).

dev\WeatherAcousticsTests (2016): real AudioClip identity, dry/wet,
partially covered roofs, cave and continuous roof change.
dev\SourceOwnershipTests (13): extracts real production Spots.PieceAt
and compiles it, verifies unique owner, saturated output, repeated
colliders and result-order independence.
dev\VerifyReleaseCandidate.ps1 enforces the full 49-file production
source inventory, forbids new frame/coroutine hook signatures,
verifies source integrations, and runs existing regression tests.

## In-game acceptance still required
- A loud machine in clear air, barely behind a wall edge, deep
  behind a single wall, deep behind multiple walls and an open grate.
  Test gradually walking both directions.
- Rain outdoors, under a full wood roof, partial roof/window,
  stone cave and wind/rain transition. Make sure *real* Valheim
  rain clip is in sync and doesn't disappear or double.
- Chest hinge, smelter/fermenter switch, crafting station and
  ambiguous shared-pivot building placements for correct origin.
- Dense-base profiler with debug logging OFF for representative
  median and 95th/99th percentile. Compare against backed-up build.
- Test doors built, opened and removed while sound is playing.
- Multiple walls, terrain, source discovery and portal graph changes
  may still cause discrete path changes; no claim of universal
  "perfect" acoustics or a completed Steam Audio clone.

## Rollback
With Valheim closed, run:
backup\V070_PROPAGATION_RAIN_SOURCE_20261122\Restore.ps1
Restores 48 original .cs files and previous Enngard/package DLL.
