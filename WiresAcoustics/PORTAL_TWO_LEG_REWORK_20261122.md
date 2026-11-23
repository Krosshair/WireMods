# WA 0.7.0 - two-leg portal validation and frequency-series propagation
Date: 2026-11-22
State: local development build; NOT public release, in-game audio/performance NOT measured.

## Source defect found
Portals.Find marks escape-ray clusters as apparent apertures. Batch checked one ray-triple from each estimated portal position to the sound source, but never verified that the current listener can reach the candidate portal. Position changes, door changes and rough aperture estimates could yield unblocked portal sounds through intervening walls.

## Changes
- Batch.Step now emits one triple of RaycastCommands from the listener to each candidate portal, SHARED by all sources in that job. This is not repeated per sound. Maximum four candidate openings = 12 extra ray commands per relevant batch, independent of number of sources.
- Initial portal-to-source tests are unchanged and retain three rays per portal/source leg.
- Batch.Apply resolves each portal entrance once, prior to sound results. Uses the existing three-ray median and Occlusion.Wall for attenuation of the first leg. Per-source path now cascades entrance-leg transmission, portal-to-source transmission, and aperture diffraction before distance attenuation.
- A blocked entrance to an approximate candidate portal is attenuated rather than automatically discarded; this avoids eliminating legitimate sound through an imperfectly estimated opening, while preventing an unattenuated leakage route.
- SpectralPaths.Cascade multiplies 125-Hz / 2-kHz energy transfers across segments (series). Separate spatially distinct paths still use SpectralPaths.Combine to add energy (parallel). This is the same conceptual distinction made in physically based propagation.
- The old 200 Hz cutoff floor was insufficient to represent multiple successive lowpasses at 2 kHz (e.g. 200 Hz + 200 Hz: equivalent single-filter cutoff ~19.8 Hz for that band). SpectralPaths.Decode can now output down to 1 Hz where needed. Unity DSP's existing first-order filter accepts positive cutoff. This is a TWO-BAND FIT, not equivalent to cascaded full-spectrum filters at every frequency.
- Existing Source/Face sound start checks, worker jobs, per-frame scheduling, cache invalidation and event handling are not replaced. No additional Update/coroutine/polling.

## Cost and test limitations
- Additional 3 * distinct portals rays per job, at most 12; versus existing up to 3 per portal PER source. Extra cost is real, but amortized; requires profiler to verify acceptable budget before release.
- Source code builds 0 errors / 0 warnings.
- Tests: compiled SpectralPaths spectral SUM/CASCADE tests (125 Hz energy and 2 kHz energy), Fresnel tests, Underwater tests, SharedEdges deterministic validation.
- No Unity runtime test of job buffer indexing, door-opening sounds, game audio, or actual CPU cost. Release is blocked pending tests.
- Remains NOT a measured HRTF/reverb/early reflections/UTD multi-edge/full band impulse response simulation.

## Rollback
Close Valheim. Run backup\PORTAL_LEG_VALIDATION_20261122\Restore.ps1.
