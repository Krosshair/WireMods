# Wire's Acoustics — wood wall transmission + Nexus sync, 2026-11-09

## Evidence (Enngard LogOutput.log, 2026-11-09 ~21:15 local, debug on)
- `wildlife sources:` while fully enclosed (100%): positional `sfx_seagull_idle` at 20–79 m, direct ray through 2.0–5.1 wall layers, `routed=True`, actual DSP gain 0.47–0.58 (about -5 to -6 dB), cutoff 576–775 Hz. So seagulls outside are processed and reach you through one wall layer at about -6 dB.
- Cause in source: `Occlusion.cs` wall weights (added 2026-11-08, changelog 0.2.3) gave ordinary wood 0.68 of a wall and hardwood/timber 0.85, below the 1.0 every structural wall had before. Through a wooden house that lets outside sounds through noticeably more clearly than in 0.2.2.
- Not a bug, checked: `AudioProvenance` transpilers apply (producer calls: random ambience 1 in 4 reports, no warnings), so the bird-inside fix for random ambience works. No Harmony errors from any Wire mod in this log. Openings/corners for a newly started blocked sound: `Occlusion` sets `LastOcc` before `Batch.Enqueue`, so routes are tried at once.

## Change
- `src/Occlusion.cs` wall weights: wood 0.68 -> 1.00, hardwood/timber 0.85 -> 1.10, ice 0.85 -> 1.00. Stone 1.25, ashstone 1.25, iron 1.35, marble 1.40, ancient 1.10 unchanged.
- `NEXUS_DESCRIPTION.txt` regenerated from the current (0.2.4) `package/README.md`; it still had the 0.2.1 text.
- No version, manifest, README or changelog change.

## Build / install
- net472 Release, 0 errors, 0 warnings.
- Live DLL before: SHA256 2A6D016832E52DF9C86C7164C92CD19546E85069F748497F71EAA37EBAB1C519 (matched backup).
- Installed: SHA256 5829590AF6906FC1951A182C006D096A99794B9016DBDA49DDE57D28289F134A. Installed with copy+rename so the Gale package cache (hard link) is not modified.

## Rollback
`backup\WOOD_WALLS_20261109\Restore.cmd` (DLL, refuses while Valheim runs), `RestoreSource.cmd` (Occlusion.cs, NEXUS_DESCRIPTION.txt).

## Needs in-game test
- Seagulls/birds outside heard from inside a wooden house: should be about 4 dB quieter per wood wall than before.
- Footstep crackle: NOT addressed (no new evidence; needs an ON/OFF output recording).
