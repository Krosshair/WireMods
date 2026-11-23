# WA 0.7.0 — Roof surface impacts + gradual exterior-field response
Date: 2026-11-23 | UNPUBLISHED RC | Game audio and performance testing remain OPEN

## User results from prior RC
- First lightweight 0-extra-ray environmental field sounds markedly better than previous sampled candidate.
- Sometimes one or two level/filter changes appeared abrupt.
- Missing actual material-dependent patter, trickle and roof vibration sounds.

## Exact source findings
- Previous Room.Measure computes the 64x3=192 ray room response on a movement/geometry-triggered ~5Hz cadence. EnvironmentalField.Gain and .Cutoff were assigned immediately after each survey. AcousticFilter then smooths via 0.15-second time constant on audio thread, which can still sound stepped during large target jumps.
- For roof geometry, Room already records the FIRST upward hit against a real authored roof prefab, and Solids.Info.C / WearNTear material is cached. A basement under an intermediate floor therefore has no roof-hit source. No additional physics queries are necessary.
- Existing ambient clip assets are broad rain loops rather than distinct wooden/metal/stone roof impact recordings. Realistic surface textures cannot be created by muffling one rain recording.

## Implementation
- Existing continuous environmental field, its 64x3 room probes and original Valheim AudioMan.m_ambientLoopSource all unchanged.
- Added FieldTau = .38 seconds exponential smoothing of rain field gain plus logarithmic cutoff BEFORE the existing .15 second audio DSP interpolation in Ambience.Glide (existing AudioMan.Update, zero new hooks). Reset the smoothing state with ambience reset and weather clip changes. Ordinary sound effects use their unchanged fast response.
- Added SurfaceImpacts.cs: FIRST provider is physically covered real roof pieces found in existing upward room survey. Material from WearNTear: wood/timber, stone/marble/ashstone, iron/metal. Most relevant nearby roof collider with hysteresis chooses one positioned rain-excitation source beneath actual roof, not at a window. EndSurvey coverage/distance sets prominence. Under floors / far from roof / during dry weather, it fades away.
- Three original synthetic stereo-independent MONO texture clips are generated as 7s 22050Hz PCM once per material and cached, using deterministic pseudo-random micro-impulses, damped resonant tones and noise. No direct WAV/OGG assets are required. The clip builder uses cheap oscillator/release recurrences instead of a trig/exp call each sample. A single optional Unity AudioSource (NOT a duplicate weather-loop clip) is emitted through the existing AudioMixer and AcousticFilter, with spatial blend .65 and fixed max 17m. Material changes fade down before switching and back up afterward; normal volume target uses a .32-second response.
- Unity AudioClip.Create / SetData are invoked reflectively ONCE at initial clip creation because these Unity APIs reference netstandard2.1, incompatible with the .NET Framework 4.7.2 build's normal compile-time symbols. The project target and references remain unchanged. Any API mismatch is caught/logged and disables ONLY the optional surface layer, never the original environment mix.
- New ConfigurationManager entries: General -> Material rain impacts (true by default); Tuning -> Material impact strength (1 by default, range 0..2).
- Debug ambient summary now exposes surface roof status, coverage, level, material, survey and switch count.
- No additional RaycastCommands, NativeArrays, new Update/LateUpdate/FixedUpdate hooks, coroutines, or periodic independent loops; reuses existing Room.Measure and AudioMan.Update.
- One new optional localized audio voice + filter during rainy roofing; actual native DSP/voice CPU impact must be measured. No claims of zero extra CPU.

## Tests
- Production target net472 built with ZERO errors and warnings. The temporary netstandard2.1 reference experiment was entirely reverted to the original csproj.
- Actual SurfaceImpacts.cs compiled with a mocked Unity API in dev/SurfaceImpactsTests; 463077 source-linked waveform/activation/looping/volume/material-change/cleanup checks pass for wood, stone and metal. Waveforms are non-clipping and have zero-valued loop boundaries.
- dev/TestSurfaceImpactsIntegration.ps1 PASS: cached geometry, 0 new rays, bounded single source, material assets, failure fallback, FieldTau smoothing and config.
- Full dev/VerifyReleaseCandidate.ps1 PASS across 53 C# files including all source ownership, SFX, atmospheric wind, analytical occlusion, HRTF and integration tests.
- No in-game listening or WiresProfiler results for new material texture; sound design still needs actual ear feedback.

## Rollback
backup/ROOF_IMPACTS_SMOOTH_20261123/Restore.ps1 (must exit Valheim before rollback), source/test/package backups and previous installed DLL.

## Test protocol
- In rainy weather go into attic directly under a wooden roof: hear a subtle local patter/trickle alongside familiar broad rain. Switch to a stone or metal roof, texture should change in tone. Step into basement below an intermediate floor; near-roof texture should fade out, while ordinary distant rain remains.
- Walk through large arches and near windows. Exterior field gain and cutoff should change without sudden 200ms steps. Object placement still attenuates immediately on audio-thread filter response for ordinary SFX.
- Optionally set Material rain impacts=false or strength=0 in BepInEx ConfigurationManager to A/B the texture.
- Capture stable control WA profiler results on roof vs basement; compare to prior SHA C49F0B39DD8C3CF0426595BA688B64BB58067268877959AE662E32D87E85447D before public release.

## Status
No public release. WS and WiresProfiler unchanged. User subjective in-game listening is the real acceptance gate.

## Installation and package verification
Production DLL SHA256: 7ED932958F6A4ED84B144DD4AB89F85DADAAD4387CD488DBE1D67EC74E99AD23
Installed Enngard: true, matching SHA256 verified
Staged ZIP: C:\Users\kajet\WireMods\WiresAcoustics\staging\WiresAcoustics-0.7.0-RC-roof-materials-smooth-20261123.zip
ZIP SHA256: 9FCD9AF69079BEEEF6DE82BE0999E87812916B6D48B697EF2F6F3312782A6E49
ZIP bytes: 218149
Contents: 5 root entries; packed DLL SHA matches tested DLL
