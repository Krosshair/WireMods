# WA 0.7.0 – Code review corrections (2026-11-23)

Status: **built, tested, and installed to Enngard; not published.** No gameplay/listening or post-change Unity profiler validation has yet been done.

## Changes
- `EnvironmentalField.cs`: same angular weights in incident and reference fields. Former 48-vs-32 normalization saturated the field prematurely. The source-linked test now includes partial-area conservation assertions (16/48, 32/48, 48/48).
- `Room.cs` + `WeatherAcoustics.cs`: isolated room-ray escapes require **both** orthogonal 8 cm offset rays to pass a neighboring wall plane (10 cm tolerance). This rejects one-offset seam escapes. `Room.Pick` now also catches the case where the centre ray passes a built seam but hits **distant terrain**, while both nearby offset rays consistently hit the same-depth built boundary.
- `Starts.cs`: defer reading the new-sound physics job until `IsCompleted`, avoiding a forced main-thread wait. Guard `Travel` and `Flush` against disabled AudioSources (possible contributor to Unity warnings).
- `Ocean.cs`: read completed shoreline jobs only after `IsCompleted` and never overwrite NativeArray buffers while a previous job is pending.
- `AnalyticWorker.cs`: remove the strict global-generation rejection for unrelated distant geometry changes. Check proximity to the ray and advance the accepted source generation after safe completion, so valid Fresnel updates do not stall forever.
- `Room.cs`: piggyback a rare room invalidation on the **existing** 30-second native-zone maintenance sweep for geometry mutations lacking recognized hooks, equivalent to 6.4 additional room physics rays/s during long stationary periods, rather than the old 192/s idle rescan. This does not guarantee instant detection of unannounced changes.
- Regression updates: `EnvironmentalFieldTests`, `WeatherAcousticsTests`, `TestRainDoorway.ps1`, `TestRainImpactField.ps1`, `TestAuditCorrections.ps1`, plus `TestReviewFixes.ps1` wired into the full gate.

## Verification
- `dev\VerifyEveryImplementation.ps1`: **PASS**, build 0 warnings / 0 errors, full deterministic regression suite.
- Full output: `dev\review-fix-regressions-20261123-final.log`.
- SHA-256 of built, packaged and installed DLL: `450F6156C9891D7F09D8291E8A8B215A1B27FD019A5657E68873087B1D4E0AF7`.
- Backup (sources, tests, package, previously installed DLL): `backup\REVIEW_FIX_20261123`.
- Enngard profile: `BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll`.
- The mod remains version `0.7.0`; **no public release performed**.

## Runtime checks required before release
1. Move toward and away from narrow windows, broad doorways and panel seams during rain. Verify no sudden false-clear outdoor field and no legitimate opening missing for most camera positions.
2. Compare roof impact texture attic / lower storey / outdoor overhang; validate old baseline is preserved.
3. Profile `Acoustics` main-thread and audio callback cost in dense and empty bases. Check for infrequent maintenance-scan spikes.
4. Check `LogOutput.log` for the disabled-AudioSource warning; guarded paths cannot establish that WA was its only source.
5. Test abrupt door/build changes, teleport/zone loads, and moving sounds while worker jobs are busy.

**Decision:** code-review findings fixed and static gates green; audible behavior, warnings and game-level performance not yet verified.
