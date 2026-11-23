# Wire's Acoustics 0.7.0 — Rain architecture reset: one virtual point source
Date: 2026-11-23
Status: DESIGN ONLY. No production code or Enngard DLL changed by this note.
Previous rain models are deprecated and are NOT release-ready.

## Objective
Replace the bespoke rain sampler/gain/roof/filter mixture with one stable WORLD-SPACE acoustic source. The source physically represents a patch of rain impacting an exposed outdoor surface, and its propagation to the character uses the existing WA wall/obstruction/opening-path physics. It is virtual: no additional Unity AudioSource, no duplicated recording. Original Valheim AudioMan.m_ambientLoopSource remains the only rain player.

## Verified current architecture
- AmbientLoop(man) is the existing game AudioSource (Ambience.cs); the current Ambience.Glide applies one AcousticFilter via Bed().
- WA's standard sound propagation is Batch.Enqueue -> batched 3-ray Occlusion + Portals + Corners, with soft/material/wall spectral transmission. Existing source-location fields must be treated carefully.
- The standard SFX pipeline explicitly excludes AcousticFilter.Kinds.Ambient / Outside from geometry scheduling in several call sites (Batch.GeometryChanged, Occlusion.Recheck, Starts etc). Setting the original 2D rain source's Kind to Sfx or moving its GameObject would be unsafe and could cause audio/spatial/other ambient transitions to break.
- Current rain is RainField.Access + WeatherAcoustics.Blend + Room.BuiltRoof + 104 extra rain-only physics commands from Room. This is the obsolete path to eliminate.
- Existing original rain loop's clip identity comes from EnvMan/AudioMan, without a bridge; any weather fade change must be performed on this source only.
- Current room ray system is 64 directions ×3 plus the added 104 rain commands. Never add per-frame Update/coroutines/polling, because the project prohibits additional frame loops.

## Proposed algorithm (to prototype and profile before installation)
1. **Emitter surface candidates**: A small, fixed set of outdoor ground patches near the listener and near exterior opening geometry. Use cached Heightmap ground coordinates where available, not ZoneSystem.GetGroundHeight (it invokes synchronous Physics.Raycast). Optional roof patch only if the real first exposed roof is closer/stronger; NEVER count intermediate floors as exposed roofs.
2. **One active virtual emitter**: Score candidates by plausible *received* acoustic contribution (source strength, physical distance, wall/direct path transmission, valid exterior exposure). Choose ONE stable world-space emitter. A window can be the sound's propagation route but MUST NEVER itself become the emission position. Keep world position fixed across motion until a clearly better real surface candidate emerges. Deterministic hysteresis prevents left/right and indoor/outdoor switching.
3. **Propagation**: Listener -> virtual emitter, using pure existing WA obstruction/material/diffraction/portals. Reuse Batch/Occlusion core when possible, but DON'T enqueue the Ambient original AcousticFilter in the normal SFX queue, which assumes the AudioSource's transform is its actual world emitter. Introduce an explicit virtual-source path input to the existing solver or factor a pure path result (gain/cutoff) from Batch, rather than duplicating the full solver. Avoid shortcut binary LOS that assumes one missed ray silences the outdoor rain.
4. **One audio output**: Existing rain AudioSource stays 2D and plays the original weather-selected AudioClip. Apply one computed transfer function (gain, low-pass) through existing AcousticFilter, along with authored rain intensity; do not write src.volume per frame or create clone AudioSources.
5. **Loss of direct sight**: An occluded emitter remains a real emitter; walls attenuate and filter it. Don't switch to a separate arbitrary ambient floor, and don't mute to zero simply because the ground is behind a window sill. Valid wall diffraction/portal paths can route around edges.
6. **Distances**: Avoid an overly strong 1/r decay from one arbitrary point (rain is distributed). Choose a calibrated, physically sensible soft-rolloff for a representative impact patch, measured against outdoor reference. Distance should be to outdoor IMPACT location, not to an opening.
7. **Transition**: Changing candidates is spatial reanchoring, not a new clip or second voice. Require a better received-energy score for a short geometry-event/motion window, optionally interpolate attenuation/lowpass without moving a live AudioSource. Native game weather fade remains authoritative.

## Remove old rain implementation as part of the actual rewrite
- RainField.GroundCommands and ScheduleGroundSight/ReadGroundSight; +104 rainy room physics rays.
- Sky/ground open fraction and cached aperture->volume arbitrary max + 2x amplification.
- Independent rain roof tricks and indirect rain gain floor (only retain roof data needed for identifying true exposed impact surface).
- Manual RainBlend/roof wall bed crossfade that bypasses actual source path results.
- Ghost/extra rain source patch system and bridge from earlier iterations must not return. Wind/other ambient routing is separate and should remain unchanged.

## Necessary engineering caution
Do NOT simply call Batch.Enqueue(AmbientLoop filter, virtualPoint) and assume it behaves as an SFX: ordinary propagation uses f.transform, f.Placed, its real AudioSource position, per-source life cycle, budgets and special cases. The source-position override must be an explicit, tested proxy path with zero extra audio voices.

## In-game acceptance tests
- In the screenshot's large arched ground floor: rain audible from visible exterior ground without walking to a window; character orientation irrelevant.
- Narrow window from oblique angle: valid sound route despite small angular aperture; avoid 'visible outside' binary ray threshold.
- Basement: rain transmitted from the actual exterior point through walls/floors; not same near-roof impact as attic.
- Attic: only real nearby exposed roof impact patch may dominate.
- Window/door open-close: propagation changes promptly without starting/stopping a source.
- Two openings: emitter switches only to materially better candidate and no jumps/volume spikes.
- Outdoors: normal game weather loudness/clarity.
- Weather selection while stationary: game source, no duplicate playback.
- WS profiler and WA CPU budget: compare controlled scene, with and without rain, and with prior version. No promises before measuring.

## Status and next step
This file is a redesign specification, NOT a shipped feature. Installed Enngard WA 0.7.0 remains the previous 2026-11-23 ground-LOS candidate until an explicitly tested replacement is built. No publication.
