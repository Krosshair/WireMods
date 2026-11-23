# Wire's Acoustics 0.7.0 — Local development build
2026-11-21. Not published to Thunderstore or Hexium.

## Build
- Source: C:\Users\kajet\WireMods\WiresAcoustics\src
- Enngard installed: AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll
- Package: C:\Users\kajet\WireMods\WiresAcoustics\dev\WiresAcoustics-0.7.0-TEST.zip
- DLL SHA-256: 0D3C390A91051B72EF71A71F3BFC401975B6EC8ABD73B9696C37F03ACB469469
- Complete rollback: backup\V070_DEV_20261121\Restore.cmd (close the game)
- Verify again: powershell -ExecutionPolicy Bypass -File backup\V070_DEV_20261121\Verify070.ps1

## Implemented and statically verified
1. Source geometry transformed into the owner's local space before caching; authored collider orientations respected.
2. Source face and chest top computed from the same oriented bounds.
3. Geometry change hooks invalidate relevant shape caches. Full rebuild clears caches without new per-frame polling.
4. Doppler exponential smoothing uses elapsed time and remains equivalent around 60 FPS.
5. The parallel direction job records world positions; directional HRTF reconstructs direction relative to the current camera pose.
6. Every available room-detected opening candidate (up to four) is verified with its own existing batched rays, instead of silently ignoring candidates three and four.
7. Path arrival-direction dispersion contributes to HRTF source width instead of canceling opposing window paths back to an erroneous point.
8. Haas/precedence localization weights transition continuously across the formerly hard 1 ms and 50 ms boundaries.
9. Openings with an estimated solid angle > hemisphere no longer generate an invalid negative radius.
10. Ocean shielding uses each sampled real shoreline bearing and its contribution to estimate received energy, instead of treating the average bearing as the only source of obstruction. The recording is not duplicated.
11. Removed an entirely unused field. AcousticFilter audio-buffer processing remains byte-for-byte unchanged.

## Preserved
- RoomScale = 0.5 (8x volume gameplay compensation)
- Same source playback clips, Harmony patches (37), and optional configuration keys.
- Existing rain audible roof floor (0.72 gain, 6500 Hz cutoff) from the immediate pre-0.7 baseline.
- Existing acoustics-related mods and sound life-cycle behavior.
- No additional per-frame loops, coroutines or global physics queries.

## Not completed / release blockers
- Actual in-game sound and performance A/B comparison has not been conducted.
- Truly spatially independent ocean waves require independent recordings or an acoustically justified distributed renderer. The existing single ocean loop is still one recording, now with more accurate multi-bearing sheltering.
- Multi-band transmission and full per-path binaural DSP are not implemented. The current renderer still uses the original two-pole filter and a width/direction reduction.
- The current 0.72 sheltered-rain floor is a chosen sonic workaround, not measured roof-impact audio or physically calibrated structural transmission.
- The number of verified window paths remains bounded by four as a performance decision. It is not an unlimited acoustic solver.
- Additional window paths may raise blocked-source physics costs; profile a dense base before publication.

## In-game validation
1. Launch Enngard. Verify BepInEx reports Wire's Acoustics 0.7.0 without exceptions.
2. Compare rotated chests, sloped objects, doors, moving creatures with the pre-0.7 build. Check voice location and accidental self-occlusion.
3. Test identical controlled movement at 30, 60 and 120 FPS. Doppler transitions should agree instead of changing with refresh rate.
4. Two windows on the same wall, then opposing windows. Move listener, close/open doors, and listen for source relocation, oscillation or unwanted broadening.
5. Test blocked sound across four openings while monitoring the Batch path counters and CPU costs. Four routes must not cause noticeable regressions in dense bases.
6. Hear ocean from a beach, peninsula, island, and inside a large shore-facing building. A single obstacle in one bearing should not mute the entire shoreline.
7. Test rain during changing weather, wooden/stone roofs, caves, open door. The baseline rain workaround remains and should not be mistaken for roof-impact synthesis.
8. Check a dense base (rain + many machines + 50+ sounds) for both frame-time and audio-callback cost with the original pre-0.7 build.
9. If any case fails, quit Valheim and run the rollback script above.

## Publication
DO NOT publish this as the final 0.7.0 without completing the in-game sound and performance gates and deciding whether the remaining source-model tasks require inclusion in this release.
