# Wire's Acoustics 0.7.0 — Valheim material and open-geometry correction
Private engineering candidate, 2026-11-22. This document records the actual local changes and unresolved limitations.

## Why
User reports practically silent loud machines and fire behind even one wall,
wooden see-through dividers or iron bars; previous partial-occlusion audible
transitions often occurred in steps.
Source audit identified that previous wall model reduced transmitted sound
using arbitrary first-wall 12–22 dB / additional-wall 6 dB plus extra
high-frequency attenuation. Some Valheim pieces have gameplay BoxColliders
that cover openings within their visual meshes, and WA interpreted them
as solid acoustic geometry. A full 16-hit physics buffer could also inject
a completely fictitious 15 dB wall.

## Authoritative acoustic reference
Valve Steam Audio C API scene material presets:
https://valvesoftware.github.io/steam-audio/doc/capi/scene.html
Valve Steam Audio source behavior:
https://valvesoftware.github.io/steam-audio/doc/unity/source.html
https://valvesoftware.github.io/steam-audio/doc/capi/simulation.html
Steam Audio combines occlusion (visible/unobstructed part of a source)
with material transmission applied only to the obstructed fraction.
The material coefficients below represent ENERGY transmission, not
linear amplitude. The source numerical values are copied from Steam
Audio's published material presets, not invented WA gains.

Valheim WearNTear.MaterialType -> Steam Audio preset:
 - Wood, HardWood, Timberwood, Ancient => wood, low .070, mid .014, high .005
 - Stone, Ashstone, Marble => rock, low .015, mid .002, high .001
 - Iron => metal, low .200, mid .025, high .010
 - Ice => glass proxy, low .060, mid .044, high .011 (material choice approximation)
 - Unknown future enum => generic, low .100, mid .050, high .030

Production WA still uses a TWO-band DSP fit (DC + 2 kHz).
SpectralPaths.Decode transforms low/mid ENERGY into an appropriate
linear gain and 2-pole low-pass. It cannot reproduce the high-frequency
Steam Audio band (8 kHz+), and DC is not a substitute for a finite-band
octave equalization. No claim of complete Steam Audio filter parity.
Each separately resolved structural wall contributes its own material
spectrum in series. The arbitrary "first 12–22 dB, next 6 dB" progression
was removed. MaxHits saturation no longer invents an extra -15 dB wall.

Approximate ONE WALL energy loss:
 - wood: low -11.5 dB; 2 kHz -18.5 dB
 - rock: low -18.2 dB; 2 kHz -27.0 dB
 - metal: low -7.0 dB; 2 kHz -16.0 dB
These are the Steam Audio preset values. Separate source distance falloff,
routing, material stacks and game-authored volumes can further reduce volume.

## Valheim-specific geometry evidence
From private local extracted Valheim 1.0.21 source/assets:
C:\Users\kajet\WireMods\ValheimSOURCE\csharp\assembly_valheim\WearNTear.cs
C:\Users\kajet\WireMods\ValheimSOURCE\csharp\assembly_valheim\Piece.cs
C:\Users\kajet\WireMods\ValheimSOURCE\ValheimProject\ExportedProject\Assets\Editor\GameElements\Pieces

Read actual prefab YAML collision components:
 - ashwood_decowall_divider: ONE BoxCollider 1x2x0.14663896 m
 - ashwood_decowall_2x2: ONE BoxCollider 2x2x0.2
 - darkwood_decowall: ONE box; decorator rendered with holes
 - piece_dvergr_metal_wall_2x2: 2 boxes, including broad 2x2x0.1
 - iron_grate: 5 boxes; one broad 1.8835 x 2.78 x 0.2, plus
   four narrow frame colliders
 - wood_fence_gate: 4 boxes; one broad 1.7463 x 1.3689 x 0.2,
   plus narrow upright colliders
 - wood_fence: 26 boxes including repeated narrow-beam shapes
 - wood_window: 2 gameplay box colliders

On known and exact prefab names, broad collision proxies spanning visible
holes are ignored for direct acoustic occlusion and room enclosure.
For iron_grate and wood_fence_gate the *narrow frame/post colliders
remain acoustic*. For wood_fence, narrow physical bars remain acoustic.
No substring-based guess extends this behavior to unknown modded pieces.
The material of retained structures is based on the real Valheim
WearNTear.MaterialType enum, not an English name guess.

IMPORTANT LIMITATION: Rendering meshes of decorative lattice were
NOT reconstructed as dedicated acoustic meshes. An ignored coarse box
cannot distinguish a ray directly through a slat from one through a hole.
The current result favors OPEN gaps rather than falsely making the
entire screen solid. This is a documented geometry-fidelity limitation,
not an exact optical/acoustic mesh solution. The room-probe currently
has only its first raycast hit; when that hit is an excluded proxy it
cannot discover a second solid wall behind the proxy. Exact-mesh ray
tracing is a separate required improvement if precision is insufficient.

## What stayed intact
 - Steam Audio-style spherical partial occlusion (adaptive up to 32,
   presently configured 16); still quantized due finite samples
 - multiple direct paths, valid diffraction/portal rays, volume Fresnel
 - character-position third-person acoustic listener and camera orientation
 - existing DSP gain smoothing and Unity AudioListener integration
 - existing scene collider enumeration and all Unity physics jobs
 - no additional per-frame Update/LateUpdate/FixedUpdate or coroutine

## Diagnostics
With Debug log enabled, the 3-second acoustic summary now also reports:
 - ignored open-panel proxy hits
 - saturated ray buffers
The detailed top-24 active sounds report the measured sample coverage,
wall gain/cutoff, exact DSP target versus actual result and waveform
meter readings. Gameplay testing/profiler needed to confirm behavior.

## Tests and release gate
 - Production net472 Release build: 0 errors, 0 warnings
 - All prior implementation regression tests pass
 - 26 source-linked compiled tests for real prefab filtering and
   Valve published material coefficients
 - Seven independent prefab YAML collider inventories and dimensions verified
 - Finite, audited source modification inventory and no new frame hooks
 - Release candidate static gate passes
 - No in-game audio listening, spectral FFT of rendered game sound,
   gameplay CPU profiling, or Steam Audio reference output comparison yet.
 - This is a PRIVATE dev build, not a validated public release.

## Paths
dev\TestValheimMaterialGeometry.ps1
dev\ValheimMaterialTests\Program.cs
dev\ValheimMaterialTests\ProductionMethods.cs (generated)
backup\VALHEIM_MATERIAL_GEOMETRY_20261122\Restore.ps1
