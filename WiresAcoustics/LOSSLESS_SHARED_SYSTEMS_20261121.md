# Wire's Acoustics 0.6.0 — shared-system refactor

Reference: published Git tag WiresAcoustics-v0.6.0 (2e32ed6740eef736628dd864f587a83db8cefe44, 2026-11-18).
Refactored on: 2026-11-21.
Release version remains 0.6.0. No public release/publish/ZIP made.
Installed in Gale Enngard and the local package; DLL hashes identical.
New DLL SHA256: 3B56FC991281205B6280B6450B4F244DC93A1FDE49F8DAA4A45A3CE699CA5F32.

## Shared implementation now in use

- RayJobs.cs: bounded persistent NativeArray storage; room/ocean one-time allocation; physics query flags; worker scheduling; three offset rays; valid-hit extraction; disposal.
  Existing callers: Batch, Starts, Room, Ocean. The same order of ray commands, budget sizes (8/4/16/16), hit counts and scheduling remain.
- AcousticMath.cs: logarithmic frequency interpolation for rain/wind, underwater, barrier transmission, diffraction and ocean. Occlusion strength/clamping reads the same setting across environments and ordinary SFX.
- AcousticFilter.For(AudioSource): existing-or-create binding shared by ambience, ocean, menu and voice chat; no playback lifecycle change.
- AcousticFilter.SmoothOcclusion: one common gain/cutoff coefficient transition now used by both binaural and plain DSP. Preserves the published arithmetic.
- Underwater.Apply: one common underwater transmission update, used by initial SFX and batched rechecks instead of duplicating arithmetic.

## Deliberately preserved

- Complete window rain and wind source behavior, source placement, position tracking, ocean direction detection, material-specific ray classification, opening and corner diffraction.
- Room reverb measurements, material absorption, atmospheric propagation, volume/distance, HRTF internals and per-sample processing.
- Same audio source creation, Play/Stop calls, source clip and config settings.
- All 37 original Harmony patch attributes and targets, with the original 0.6.0 version GUID/manifest unchanged.
- No custom Update, LateUpdate, FixedUpdate, or additional per-frame polling behavior introduced.

## Verification and limitations

A frozen pre-edit copy of the published 35-source-file codebase and deployed DLL is at backup/LOSSLESS_REFACTOR_20261121.
The repeatable parity test is backup/LOSSLESS_REFACTOR_20261121/VerifyParity.ps1.
All 42 static/source parity checks passed; a full clean Release build succeeded with 0 errors and the original CS0649 warning for AcousticFilter.StartHeld.
This is **structural code deduplication**, not numerical tuning and not binary compression: compiled DLL size is unchanged (129536 bytes); two small shared helper files increase raw source size slightly.
Exact runtime DSP output parity is NOT established without in-game comparison. Worker-job submission through a shared method may have a different microscopic execution cost, though no ray count or event order was intentionally changed.
No gameplay listening test or profiling was performed here.

## Rollback

To restore the unmodified published source and Enngard DLL: close Valheim and run
backup/LOSSLESS_REFACTOR_20261121/Restore.cmd
