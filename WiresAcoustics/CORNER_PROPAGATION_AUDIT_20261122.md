# WiresAcoustics corner-path spectral architecture pass
Date 2026-11-22. Status: local development build, NOT release.

## Independent source finding
Steam Audio C API direct effects maintain separate occlusion, transmission and spectral effects. Wwise spatial audio separates obstruction, diffraction and transmission. Serial propagation components require multiplying their spectral transfer; independent incoherent routes add energy.
References:
- https://valvesoftware.github.io/steam-audio/doc/capi/direct-effect.html
- https://valvesoftware.github.io/steam-audio/doc/capi/guide.html
- https://documentation.help/Wwise-Unity/pg__obs__occ.html

In current WA, Batch.cs had a contradiction:
- portal two-leg traversal used SpectralPaths.Cascade;
- corner path used cg*=bg and cc=Mathf.Min(cc,bc);
- best-corner comparator Consider used g*=bendGain and cut=Mathf.Min(cut,bendCut);
- illuminated known edge branch used gain*=litGain, cutoff=Mathf.Min(cutoff,litCut).
The minimum-cutoff shortcut fails to represent multiple serial frequency-dependent losses: the second filter's contribution is dropped whenever the first has lower cutoff. Both corner aggregation and winning-corner attribution must use the same spectral calculation.

## Implementation #1: Corner path attenuation
- Batch.cs winner Consider and measured corner route now call SpectralPaths.Cascade(gain,cutoff,diffractionGain,diffractionCutoff,...). Existing additional geometric length attenuation remains a gain multiplication, not a spectral filter.
- Immediate verification: 0 warnings/errors net472; independent source callsite checks and 400 gain/cutoff pairs in dev/TestCornerRouteParity.ps1; renderer parity regression also passed.

## Implementation #2: Clear-side diffraction
- Batch.cs clear side now also cascades direct and edge transfers. This is consistent with the serial-transfer representation used for blocked edges and portals.
- Immediate verification: 0 warnings/errors, corner+renderer parity regression.
- Physically this remains an APPROXIMATE two-band spectral fit, not full wave or phase interference.

## Implementation #3: Finite obstacle spectral normalization
- Diffraction.RoundObstacle now calls the central spectral decode with nonindependently-clamped 125/2000 Hz energies. Decoder caps overall energy with ONE scale factor and preserves their relative ratio.
- Immediate verification: 0 warnings/errors, 354 independent renderer parity checks, 38 diffraction checks. A direct PowerShell reflection test could not execute UnityEngine.Bounds outside Unity (ECall), so a separate .NET harness links ACTUAL production Diffraction.cs and SpectralPaths.cs with deterministic Unity Vector3/Bounds replacements and compares independent physical attenuation calculations for 75 obstacle geometries. All pass.
- In this specific three-path model, minimum 5dB loss/path implies combined intensity <= ~.949, so the independent-clamp issue is presently mostly a consistency cleanup; no audible difference is claimed without measured in-game conditions.

## Full regression
dev/VerifyEveryImplementation.ps1 now also includes TestCornerRouteParity.ps1 and ObstacleTests. Full suite passed: independent renderer 354 checks, diffraction 38, corner 400, 75 obstacles, Fresnel and underwater, geometry shared cache, 100000 meter packets, acoustic trace, area invalidation.
No new Update/FixedUpdate/LateUpdate, coroutines, source scans or polling.

## Still NOT verified or implemented
- Full multi-edge diffraction/room/portal topology, measured HRTF, geometric early reflections, convolution reverb, broad frequency material phase effects.
- No in-game audio listening, full Valheim mod-stack conflict tests or CPU profiling.
- This is NOT a claim of perfection or full Steam Audio/Wwise parity.

## Rollback
Close Valheim and run backup\CORNER_SPECTRAL_PARITY_20261122\Restore.ps1.
