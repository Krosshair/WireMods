# WA 0.7.0 - Original A_/_A one-audible-loop bug
Date: 2026-11-22
Status: development fix installed into Enngard, not publicly released. Game listening test pending.

## Confirmed in actual Valheim assembly
Decompiled D:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll.
AudioMan.UpdateLoopingConcurrency groups ZSFX loops by m_hash, sorts by camera distance every 16 frames.
When m_maxConcurrentSources is 0, its i > maxConcurrentSources check leaves only index 0 enabled.
Contradiction: GetLoopingMaxConcurrency uses forcedMaxConcurrentLoops (typically 5) when zero, but UpdateLoopingConcurrency uses zero literally.
ZSFX adjusts m_concurrencyVolumeModifier to 0 over 0.5 seconds for disabled loops; AudioSource.isPlaying remains true.
As the nearest sound changes, audible source fades between A_ and _A. This is a concrete code path, not speculation about HRTF or phase.

## Fix
Starts.cs: Harmony Prefix on ZSFX.ConcurrencyDisable selectively rejects suppression for independent positional world loops.
Calls ZSFX.ConcurrencyEnable and skips Disable for spatialBlend >=0.5 looping 3D sources when WA enabled.
Preserves vanilla concurrency for 2D audio, one-offs, random world ambience, voice, UI, general ambient and outside loops.
Preserves original fire range-based loudness, all authored clips, and source positions.
No new per-frame hooks, no polling. Existing game concurrency checks now leave independent world emitters audible.
Previously muted world loops recover at the next vanilla loop-concurrency check.

## Verification
Source-extracted behavioral test PASS: two matching world loop instances both enabled; exclusions and mod-off preserve vanilla.
Full VerifyReleaseCandidate: PASS; full VerifyEveryImplementation: PASS; release build 0 warnings/errors.
Installed development SHA256: 517532AB5C5013D6BB3527F0269CF3B4CA57C31F4C4415673F774A219874B397.
In-game hearing and profiler frame time NOT YET VALIDATED on this exact build.

## Rollback
backup\POSITIONAL_LOOP_CONCURRENCY_20261122\Restore.ps1 (Valheim must be closed).
No version bump or public release.