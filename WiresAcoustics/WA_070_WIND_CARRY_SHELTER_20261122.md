# Wire's Acoustics 0.7.0 — wind-carried world sound respects wind cover
Date: 2026-11-22
Status: release-candidate development revision; no public release. In-game confirmation pending.

## Problem and prior behavior
Directions.Step already applied atmospheric wind refraction to positioned world sound sources:
  +3 dB downwind at full wind and long distance;
  -10 dB upwind at full wind and long distance;
  starts at 15 metres, reaches full effect at 112 metres.
But it used EnvMan wind intensity alone, even inside a sheltered building or behind upwind cover.
Ambience separately already evaluated local wind exposure using Room.Exposure and 64 cached room rays. These data were not used by world-sound propagation.
When the player stayed stationary as the actual wind direction rotated, Room.Exposure could remain stale because geometry was deliberately not re-raycast.

## Implemented
- Directions.WindCarryDb reuses exactly the existing numerical gain/attenuation model; multiplies its effect in dB by Ambience.WindExposure (0 = completely sheltered, 1 = exposed).
- Same natural distance and air attenuation, independent muffling/through-wall signal transfer and normal HRTF unchanged.
- All positional world sounds tracked by Directions receive the factor: machinery/fires/creatures/combat/situated voices. Music, UI and unpositioned 2D ambience are excluded.
- Room.WindSurveyReady guards against reading uninitialized ray results; defaults to open air before a completed survey.
- Ambience returns current smoothed local wind exposure. This is the same shelter attenuation used for the regular wind and the Plains reed/wind ambient recordings.
- Wind direction rotates at a stationary listener: Ambience.Pan refreshes directional weighting from existing Room rays only when heading changes by approximately 7.3 degrees. No new raycast, GameObject, worker task, coroutine or Update hook.
- Wind disabled => zero wind contribution. Muffling disabled => full exposure (no geometric wind shelter; wall muffling itself is separately disabled). Normal source level unchanged.
- Starts and independent machine/fire loop behavior, wind-texture fix, and prior removal of abandoned lifecycle fade remain unchanged.

## Model scope and limitations
- This is an approximate outdoor propagation amplitude effect, not full time-domain advected wave simulation / explicit atmospheric refraction solver.
- Uses listener-local shelter (as requested) for all positioned emitters. It does not sample the wind exposure surrounding each remote sound source or along the entire path. Acoustical occlusion remains separately calculated by existing rays.
- At 15 metres or closer the carried-sound effect is zero. A windward effect develops with distance; rapid camera turning alone will not create level changes unless the wind or acoustic geometry changes.
- Very strong wind can cause a noticeable attenuation difference at tens/hundreds of metres, not exaggerated nearby.

## Validation
- Production Release net472 build: PASS, 0 warnings/errors.
- dev/TestWindCarryShelter.ps1 compiles the exact production model method and tests the sound direction/range, 0%, 50%, 100% exposure, disabled switch, still air and saturation.
- VerifyReleaseCandidate.ps1 PASS, complete source-linked regression suite PASS across 50 C# production files.
- Reference output: RELEASE_CANDIDATE_VERIFICATION_20261122.txt.
- In-game listening/CPU and indoor rainfall still OPEN.
- Installed DLL SHA256: 81153FF3EF2A330D8A0DC9C8B8A66F8C3CE52092E9A180EE2A6A4227F934987D.
- Prepared package: staging/WiresAcoustics-0.7.0-RC-wind-carry-rain-pending.zip.

## Restore
- backup/WIND_CARRY_SHELTER_20261122 contains source, packaging and previous installed DLL.
- backup/WIND_CARRY_SHELTER_20261122/Restore.ps1 (must be used while Valheim is closed).
