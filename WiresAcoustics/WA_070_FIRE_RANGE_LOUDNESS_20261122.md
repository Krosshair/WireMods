# WA 0.7.0 — Restore fire size hierarchy from Valheim sound range
Date: 2026-11-22
Status: developer build installed into Enngard; no public release. Gameplay acceptance / exact prefab ranges not yet measured.

## Why
User corrected prior blanket removal of fire attenuation. A brazier should naturally remain louder than a sconce. The source attenuation should be based on Valheim's own authored AUDIO range, not on the physical collider dimensions. Removing the previous custom -14..-18 dB size-driven level completely was an incorrect interpretation of the polyphony complaint.

## Changes
- Area.Measure determines whether the source is a loop attached to a Fireplace owner and caches FireLoop once.
- The source level of a fire loop derives from f.AuthoredMaxDistance — original AudioSource.maxDistance rather than the maxDistance extended by WA's Realistic distance.
- The game range is normalized against a 10 m reference fire: gain = min(1, originalMaxDistance / 10m). Example: 2m=>0.2 (-14dB); 3m=>0.3 (-10.46dB); 5m=>0.5 (-6.02dB), >=10m=>1 (0dB). Invalid/nonpositive ranges revert to 1, not silence.
- 10 m is the engineering reference chosen for full-size fire, NOT a measured maxDistance for every prefab. It sets a common amplitude baseline. Range alone does not physically specify exact sound pressure, but the ratio is consistent with a 1/r attenuation model.
- FireLoop is re-applied on Area.Measure after master toggle so a previously cached loop doesn't stay unintentionally full-volume.
- Collision shape still influences sound origin, emitter area and HRTF width only.
- No changes to clip count, Unity voice budget, loop phase decorrelation, machine loudness, wind or world ambient beds.
- Debug-only polyphony log now includes authoredRange for each duplicate looping clip, enabling exact in-game verification for sfx_fire_loop and SFX_propeller.

## Tests
- Source-linked math extraction test: 1,2,3,5,10,20,0,-1m plus NaN guards PASSED.
- Production .NET Framework 4.7.2 Release build: PASSED, zero warnings/errors.
- New static source-path checks in dev/TestPolyphonyRegression.ps1 and dev/VerifyReleaseCandidate.ps1.
- Existing full regression suite: ALL IMPLEMENTATION REGRESSION TESTS PASS, including source-linked spectral/acoustic tests and new fire-range math extraction test.
- VerifyReleaseCandidate.ps1: RELEASE CANDIDATE STATIC GATE PASSED, 50 production files; build SHA256 F5DC7C049852D90A74103E5CF94E3F17C0E051EDDEDA0AF7A3141F7F5D2E4D88.
- The built development DLL was hash-verified and installed into Enngard (Valheim closed); Debug log remains false and Random loop start true. No in-game perceptual or profiler measurement of this exact build, no publish.

## Next gameplay check
With Debug log normally OFF, stand between sconce and brazier and rotate slowly. Both must stay independently audible; the sconce must remain quieter according to its authored max range. Verify separate linen machine loops with phase randomization already restored. If still wrong, enable Debug log temporarily; [acoustics.polyphony.source] contains per-source authoredRange, priority, play/virtual state, phase samples, WA source gain and output RMS.
If two fire sources have equal game-authored maxDistance, the range-only formula intentionally gives them equal additional gain — do not invent a size difference without an additional authored parameter.

## Backups
backup/FIRE_RANGE_LOUDNESS_20261122 includes previous Area.cs, Filter.cs, AcousticTrace.cs, installed DLL and verification scripts.
rollback: powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\kajet\WireMods\WiresAcoustics\backup\FIRE_RANGE_LOUDNESS_20261122\Restore.ps1 (Valheim CLOSED).
