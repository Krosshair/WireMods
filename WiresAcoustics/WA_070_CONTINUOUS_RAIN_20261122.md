# Wire's Acoustics 0.7.0 — Continuous Rain: Indirect + Direct
Date: 2026-11-22
Status: unpublished release candidate; in-game listening and audio/multimod performance still NOT verified.

## User feedback and root causes
Rain sounded delayed and disappeared when listener moved away from windows. Verified two code-level causes:
1. RainField.Glide was gated by IsActiveRainLoop (the currently playing AudioMan ambient clip matching EnvMan's new wet-weather clip). Valheim AudioMan.UpdateAmbientLoop first fades OUT the previous recording, switches the clip, then fades IN the target. This introduces up to several seconds of weather onset delay. It was incorrect to gate WA's rain on that sound source change.
2. The main original 2D rain bed was mixed by s_rainGain * (1 - rain field RMS visibility), reaching ZERO whenever directional sectors were fully exposed. When room ray visibility changed or became blocked, the previously opened directional voices faded while the bed could remain at zero. Independently, a natural cave without builtRoof could reduce the rain bed nearly to silence. This confused transmission with visibility.
3. Direct sector transitions were linear for up to .25 sec on top of the room's cached 0.2-second survey interval.

## Current implementation
- Selected rain target now comes from EnvMan's current m_isWet / m_ambientLoop / m_ambientVol, rather than waiting for AudioMan's audible ambient clip to swap. That setting is the authoritative weather target.
- Added one temporary 2D rain bridge with the selected original game clip. Its level smoothly compensates for how far AudioMan's own wet ambient source is from the selected volume. Once the original recording reaches its target gain, bridge level approaches zero and it stops. At rain exit, both bridge and sector voices fade rather than suddenly stopping or trying to play a null clip.
- Separate continuous INDIRECT rain heard through walls/structure/roof. WeatherAcoustics.Blend now maintains an indirect minimum of .24 while the rain target is selected, and a roof-specific gain up to .72. Both use the original room-measured spectrum and actual roof/wall geometry; this is a perceptual safety floor, not a physics-measured transmission coefficient.
- Separate DIRECT rain field in exposed outdoor regions. Keeps the existing 8 regions / max 4 selected areas from existing 64 room ray survey, no additional raycasts. Direct energy is capped to .75 of the original field, and its region-level response was raised to 6 per second (<=125ms for the maximum .75 target after each survey).
- Bed/direct balance follows a constant-power remainder, s_rainGain * sqrt(1 - impactMix^2), never zero due to a full direct region because impactMix <= .75; minimum ~66.1% of rain-bed gain even with fully opened direct regions. Where there are no outdoor-facing rays, impactMix=0 and the indirect layer remains audible.
- The previous doorway sources still route other ambient textures and wind; rain is not artificially emitted by a single door. Wall acoustics, physical material paths, weapon visual positioning and WS were not changed.
- Same existing AudioMan.Update/Room callbacks; NO new Update/LateUpdate/FixedUpdate/coroutines, native raycasts or new physics jobs. A temporary single 2D bridge AudioSource adds one audio voice during weather crossfades, using the original ambient mixer and recording.

## Expected listening checks
1. Play in active rain and stand under a roof near an open doorway; exterior direct rain should be prominent.
2. Walk several metres away, so room rays no longer see outdoors; direct rain should fade, indirect rain should remain consistently present and more filtered, not vanish.
3. Walk back and forth across the doorway. Geometry transitions should not lag by a full second; the room's original survey still occurs at 5 Hz, plus worker completion.
4. Stay stationary while weather changes from clear to rain; first rain should be audible without waiting for Valheim's two-second ambient fade-out and fade-in.
5. Compare rain through wooden/stone rooms and in a sealed cave; structural filtration should remain, not a magical always-loud outdoor recording.
6. Capture WA CPU and WS timings with Wire's Profiler; additional bridge voice only exists during transitions.

## Verification
- Main net472 production build: 0 errors / 0 warnings.
- WeatherAcousticsTests: source-extracted numeric weather bridge cases, 250ms transition, independent indirect floor, roof blend/continuity tests.
- dev/TestContinuousRainLayers.ps1: source-linked tests of real weather selection, no game audio clip gate, volume bridge, direct response, floor, constant power, null-clip safety and no extra rays.
- Existing dev/TestRainImpactField.ps1, dev/TestRainDoorway.ps1 updated to assert new source/event behavior.
- Full VerifyReleaseCandidate and nested implementation suite across 52 production C# files: PASSED. 0 errors, 0 warnings. Actual results are in RELEASE_CANDIDATE_VERIFICATION_20261122.txt.
- Production DLL SHA256 F9BBB66E81A053CEB3FB9EEE18DEF88511A34059C6F3264EF0365B52878A263C, installed in Enngard and package.
- Staged ZIP staging/WiresAcoustics-0.7.0-RC-continuous-rain-20261122.zip, SHA256 542DB21931D78F2726A566B8A88678F5D3F45E9493390D0C85F383B0043AA0AD, five required root entries verified. Not published.
- In-game weather onset, audible attenuation, native audio mixing overhead remain OPEN; neither release nor public tag created.

## Rollback
Complete pre-change src/dev/package/Enngard DLL at backup/RAIN_DIRECT_INDIRECT_20261122.
Run backup/RAIN_DIRECT_INDIRECT_20261122/Restore.ps1 ONLY with Valheim closed, then rebuild before any further install.
