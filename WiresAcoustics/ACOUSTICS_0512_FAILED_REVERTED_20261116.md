# Wire's Acoustics 0.5.12 FAILED EXPERIMENT — reverted 2026-11-16

## Status
The user tested the 0.5.12 modifications and explicitly reported **the acoustics update does not work**.
Both enhancements are considered failed/unverified and were reverted on request. Do not publish or reinstall this build without redesign and in-world acceptance tests.

## Attempted changes
1. Component-specific sound placement: smelter ore/fuel, fermenter add/tap, cooking-station fuel, shield-generator fuel, incinerator lever, chest open/close at the lid, door hinge, fireplace fuel/toggle, armor stand. Implemented by event-scoped Harmony patches on Valheim's real effect-producing methods plus exact EffectList identity checks, under src/InteractionOrigin.cs and Spots.cs.
2. Source-bounds occlusion: check six face centers and eight AABB corners, three-ray seam checks, and consider source clear if any sample is clear. Intended to keep source location/HRTF unchanged. Implemented under Area.cs, Batch.cs, Occlusion.cs, Filter.cs and Follow.cs.
3. User reported specifically that chest closing did not benefit from bounds-aware occlusion. The later owner-propagation patch and immediate samples were not verified in-world either.

## Why the approach is retired
- Building successfully and loading in the main menu did NOT establish that actual sounds played from the intended parts or that bounds-clearance reached the audible filter in the first frame.
- Main issue in earlier implementation: EffectList.Create sometimes had baseParent=null. Owner inference could fail; the patch attempted to reconstruct it via producer Harmony context, but user reports feature still does not work.
- Additional worker-ray checks on short one-shot sounds might complete after playback and be inaudible. Immediate checks had a frame-budget guard and were never verified in a world.
- Tests for nested SFX, gameplay lifecycle timing, wall seams, source routing, and alternate multiplayer origin remain absent.
- No root cause proven for user-observed failure; do not mark either feature fixed.

## Rollback verified
- Restored original 0.5.11 source: Spots.cs, Area.cs, Filter.cs, Occlusion.cs, Batch.cs, Plugin.cs, Follow.cs.
- Removed 0.5.12-only InteractionOrigin.cs.
- Restored package README.md, CHANGELOG.md, manifest.json, WiresAcoustics.dll.
- Restored Enngard-installed 0.5.11 DLL and metadata.
- Rebuilt 0.5.11: 0 errors, 0 warnings.
- Preserved failed 0.5.12 implementation in backup scripts and ACOUSTICS_0512_WORKLOG.md. See backup/SMELTER_BOUNDS_20261116 and scripts backup/patch_sound_bounds_20261116.ps1, backup/extend_interactions_20261116.ps1 and backup/bounds_startup_20261116.ps1.
- Published 0.5.11 was never replaced by 0.5.12.

## If revisited
Instrument exact EffectList.Create / ZSFX.Play path and final occlusion coefficients for chest closing in a real game. Establish causal source owner and sound playing duration. Avoid broad patches, extra per-frame hooks, or blind assumptions.
