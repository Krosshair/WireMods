# Wire's Acoustics - Fresnel physical-geometry and renderer parity
Date: 2026-11-22
Status: DEVELOPMENT BUILD ONLY; not equivalent to complete Steam Audio/Wwise acoustics.

## Independent references
ITU-R P.526-16 sec. 4.1 ideal single knife-edge:
https://www.itu.int/dms_pubrec/itu-r/rec/p/R-REC-P.526-16-202511-I!!PDF-E.pdf
v = h sqrt(2/lambda * (1/d1 + 1/d2)), d1,d2 are distances
ALONG the source/receiver path from the idealized screen to the endpoints.
Valve Steam Audio distinguishes frequency dependent transmission from
geometrical occlusion and indirect propagation:
https://valvesoftware.github.io/steam-audio/doc/capi/direct-effect.html

## Implementation 1: oblique-incidence Fresnel distances
ContinuousEdges.cs was sending the perpendicular projection of each
listener/source displacement onto the wall normal to FresnelEdge.Parameter.
At an oblique incidence, those distances are shorter than the actual
source-screen / screen-listener path segments, exaggerating diffraction.
Now uses t = normalized segment crossing fraction at the validated wall
plane, d1 = |source-listener| * t and d2=|source-listener|*(1-t).
This is a justified correction WITHIN the idealized half-plane/paraxial
approximation. It does NOT solve arbitrary 3D wave or multi-edge propagation.
Validated immediately after implementation: 0 error/warning build and
existing Fresnel numeric unit tests.

## Implementation 2: material transmission and actual two-pole DSP
FresnelEdge.Transfer used to approximate material high-band amplitude as
T_low / sqrt(1+(2000/cutoff)^2), but WA's audio output filter is
TWO cascaded sample-rate-dependent one-pole stages with a 20k bypass.
Correct high-band pressure is now T_low * sqrt(SpectralPaths.Response(cutoff)).
Both path transmission and modeled edge sound are fitted using the
ACTUAL digital two-stage response shared with all other propagators.
Validated immediately after implementation: 0 error/warning build and
existing Fresnel mathematical checks.

## New independent test: dev/TestFresnelGeometryParity.ps1
- 36 source-listener/screen geometries with 4 obliquities (144
  geometry/ITU equation assertions).
- 6 cutoff settings, 5 material gains, 3 edge clearances, and
  comparisons against independent direct Fresnel complex-intensity output
  and compiled SpectralPaths measured high-band response (180 assertions).
- 324 mathematical assertions total.
- The allowed high-band error .0006 reflects the existing DSP's deliberate
  bypass discontinuity at 20k, which means certain desired high-band energies
  cannot be represented exactly by a single cutoff at those frequencies.
- Verifies production ContinuousEdges.cs calls PathDistances and production
  FresnelEdge uses actual two-pole Response() rather than an approximation.

## Implementation 3: sample-rate invariant angular-frequency constant
SpectralPaths.cs computed Math.Cos(2*pi*f/fs) repeatedly on each
Response() call, although that value changes only when AudioSettings
output sample rate changes. It is now cached in s_bandK and recomputed
only via the existing SetSampleRate() call when the sample rate changes.
The digital transfer equation and branch behavior are unchanged.
Validated immediately: 354 independent digital-renderer parity checks at
44.1, 48, 96 and 192 kHz, plus 324 Fresnel geometry/material checks,
all pass; 0 compiler warnings/errors. No new per-frame hooks.

## Regression
dev/VerifyEveryImplementation.ps1 now includes Fresnel geometry parity.
Full tests pass: geometry, 2-pole DSP, Fresnel, diffraction, underwater,
corners, finite obstacle, shared edge cache, 100000 packet audio meter,
acoustic logging, source shape invalidation.
No user/gameplay test performed.

## Still incomplete; not a perfect build
Complete room/portal connectivity, multiple diffraction edges,
measured HRTF datasets, impulse-response reverb, full-band/phase
propagation and Unity/audio-device runtime performance remain unresolved.
The Fresnel model assumes an infinite thin half-plane and uniform
zero-phase wall transmission, appropriate only under its stated
geometric assumptions. There is no validated in-game localization or
listener judgment.

## Rollback
Close game and run backup\FRESNEL_GEOMETRY_PARITY_20261122\Restore.ps1.
