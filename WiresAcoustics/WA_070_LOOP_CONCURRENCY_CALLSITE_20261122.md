# WA 0.7.0 — independent positional loop sources
Date: 2026-11-22. Local Enngard dev build only; not released.

## Actual vanilla source selection
AudioMan.UpdateLoopingConcurrency iterates the same-hash ZSFX sources by camera distance every 16 frames.
With m_maxConcurrentSources=0, comparison (i > 0) disables all except first source.
ZSFX.UpdateSFX then smoothly reduces m_concurrencyVolumeModifier toward zero over 0.5 seconds.
Thus identical fires/linen machines may be playing but only one is audible, with A_ <-> _A as nearest changes.

## Correction to prior fix
Earlier patch targeted ZSFX.ConcurrencyDisable(), a trivial setter. This may be JIT-inlined, bypassing Harmony.
New targeted patch in Starts.cs: Harmony Postfix on AudioMan.UpdateLoopingConcurrency itself.
Immediately after vanilla's periodic selection, call ConcurrencyEnable on each actual positional world loop.
Also force ConcurrencyEnable after ZSFX.Play to avoid initial registration suppression.
Music, 2D, world random ambience, UI, voice, abstract ambience and one-shot concurrency are unmodified.
No new Update/LateUpdate/FixedUpdate hook, no coroutine; postfix follows game's own existing every-16-frame update.
Random loop start (already implemented) remains active per source: independent random timeSamples when accepted for playback.
Does not change game-authored volume, fire loudness derived from original range, or clip pitch.

## Tests
Production source-extracted test dev/TestWorldLoopConcurrency.ps1 PASS: same clip both playing, recovery after vanilla suppression, exclusions, mod-off and phase integration.
Full release candidate static gate PASS: 50 production files, compilation zero warnings/errors.
Installed DLL SHA256: 1D1F2268D4CF00740E2F767D575AAA8BA48871209DEE302FA83799D3E06BABAF.
Runtime A_ -> AA auditory check unverified until played in-game. No public release.

## Rollback
backup\LOOP_CONCURRENCY_CALLSITE_20261122\Restore.ps1 (with game closed).