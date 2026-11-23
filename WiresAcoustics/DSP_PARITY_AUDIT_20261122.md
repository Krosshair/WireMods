# Wire's Acoustics 0.7.0 - independent DSP parity + diagnostic consistency pass
Date: 2026-11-22
Status: installed DEVELOPMENT TEST BUILD, NOT a release or complete 3D acoustics engine.

## Confirmed defect: propagation cutoff was fitted to the wrong DSP
Production code previously calculated path high-band attenuation as if one
continuous-time first-order lowpass were rendered. The actual Filter.cs output
is two cascaded discrete-time filters:
a = 1 - exp(-2*pi*1.5538*cutoff/sampleRate)
|H1|² = a²/[a² + 2*(1-a)*(1-cos(2*pi*f/sampleRate))]
|H2|² = |H1|^4.
It bypasses those poles entirely at cutoff >=20k.

Updated SpectralPaths.cs to:
- evaluate the ACTUAL two-pole discrete-time response, including the bypass;
- fit an inverse cutoff analytically for target DC + 2-kHz path energies;
- receive current output sample rate from existing Filter.RefreshOutputFormat;
- use same FilterStage constant as the renderer (AcousticMath.FilterStage);
- preserve path spectral ratio when the energy of several paths exceeds unity.
The audio callback's filter coefficient and loop are UNCHANGED.

Diffraction.cs now stores per-edge physical high-band energy (rather than a
cutoff fitted to the wrong DSP), and converts the final diffraction pair
to the exact renderer response for RoundDetour/LitEdge/Opening/RoundObstacle.
FresnelEdge.cs also uses the same decoder.

Tests:
dev\TestRendererParity.ps1: 354 independent math checks at 44.1,48,96,192 kHz,
DC & 2-kHz cascades and parallel path sums, saturation spectrum preserved.
dev\TestDiffractionParity.ps1: 38 checks that Maekawa/Kurze formula at sampled
detours translates to measured renderer's two-band response.
Existing numerical Fresnel, Underwater and Spectral tests retained.

IMPORTANT limitation: a TWO-POINT fit does not recover the complete frequency
response of arbitrary multi-layer transmission or a full impulse response.
At cutoff=20k there is an intentional bypass discontinuity in the EXISTING DSP.
No general phase model, multi-edge field solver or native reverb engine is added.

## Confirmed defect: diagnostic meter was not thread-safe
Previously the audio callback accumulated two double fields while the main
thread read and reset them independently. A three-second log could mix output
from one interval and input from another.

AudioMeterHistory.cs stores COMPLETE callbacks in a bounded lock-free
per-source packet ring. Audio thread publishes input energy, output energy and
sample count together with an atomic completion version. The main thread
reads only self-consistent packets; overwritten slots are counted/dropped and
identified in the log as meterDropped. Audio debug is enabled/disabled by
the existing Budget.Step every .25s so disabling debug doesn't leave a costly
meter silently active. Packet storage is allocated on the main thread, not
in the audio callback. No new Update/FixedUpdate/LateUpdate or coroutine.

Tests:
dev\MeterHistoryTests: publishes 100,000 packets concurrently with an active
reader, verifies ratios/counts, partial-write rejection, and overflow accounting.
dev\AcousticTraceTests: verifies logged synthetic trace contents including
actual sampled DSP values. Log analyzer recognizes dropped meter packets.

WARNING: This is a source metadata / per-source pre-mix meter, NOT a capture
of Unity's final stereo mix, final HRTF output, OS spatial processing, or
what a particular user subjectively hears. It cannot reveal crackles from
external audio hardware.

## Remaining architectural limits
- Audio propagation still uses two energy bands and one gain+cutoff DSP.
- No complete 3D mesh/room topology or guaranteed multi-edge diffraction.
- Estimated portals from 64 ray directions can omit or misclassify an aperture.
- Reverb uses Sabine-inspired room approximation and AudioReverbZone;
  does not implement geometric reflections, impulse response or convolution.
- HRTF is synthetic, not a measured SOFA dataset.
- Native game mod stack and in-game CPU/audio performance have not been tested.

These are honest blockers to calling the mod equal to a mature dedicated
physics-based renderer, and to any claim of zero weaknesses or regressions.

## Rollback
Close Valheim and run backup\DSP_PARITY_20261122\Restore.ps1
