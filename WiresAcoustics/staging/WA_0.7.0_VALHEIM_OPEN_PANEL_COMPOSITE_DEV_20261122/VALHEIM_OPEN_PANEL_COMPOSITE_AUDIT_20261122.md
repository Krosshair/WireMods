# WA v0.7.0 — VALHEIM-ADAPTED OPEN-PANEL COMPOSITE
2026-11-22. PRIVATE DEVELOPMENT ONLY.

## Why this change
Original 0.7.0 geometry exemption discarded the large gameplay collider
of decorative carved panels, wood fence gates, and iron grates. That
made objects 100% acoustically transparent across their broad collider.
Conversely, earlier WA made all panels nearly opaque and could entirely
mute heavy machines behind visually open barriers.

## Industry acoustic basis
ASHRAE Handbook, 2025 Fundamentals, Ch 8, composite partition
transmission: area-weighted TRANSMITTED ENERGY. When the open fraction
is phi and remaining solid area has transmission coefficient tau:
   tau_eff(f) = phi + (1 - phi) * tau_solid(f)
This is a physically established composite-partition model, not an
arbitrary linear volume mix, floor, or correction blend.
https://handbook.ashrae.org/Handbooks/F25/IP/F25_Ch08_ip.aspx
Steam Audio models occlusion as fraction of source visible and material
transmission affecting only the occluded portion:
https://valvesoftware.github.io/steam-audio/doc/capi/guide.html
https://valvesoftware.github.io/steam-audio/doc/unity/source.html
Steam Audio material energy fractions from:
https://valvesoftware.github.io/steam-audio/doc/capi/scene.html

## Valheim-specific content
Use ValheimSOURCE 1.0.21 shipped prefab IDs, dimensions and colliders
for a bounded list of visually open building pieces:
 - ashwood_decowall_divider
 - ashwood_decowall_2x2
 - darkwood_decowall
 - piece_dvergr_metal_wall_2x2
 - iron_grate (broad gameplay panel only, retain narrow frame)
 - wood_fence_gate (broad gameplay panel only, retain posts)
 - wood_window
The wood_fence slats are already represented by multiple narrow
colliders and are kept as physical geometric acoustic hits.
No substring-based pattern matching applies to arbitrary modded pieces.
Prefabs locally inspected:
C:\Users\kajet\WireMods\ValheimSOURCE\ValheimProject\ExportedProject\Assets\Editor\GameElements\Pieces

## Mesh-informed per-prefab open fraction
OpenPanelArea.cs looks at the actual MeshFilter rendered triangles
in a prefab instance and projects their 2D union onto the panel plane
once per prefab/collider type using a 32x32 sampling grid.
This is content analysis and not an additional acoustic physics law.
One cached open fraction is reused across all identical prefabs;
no per-frame mesh rasterization and no Unity physics jobs added.
This avoids reconstructing thousands of triangles as raycast colliders.
When meshes are unreadable or unavailable, a clearly named
visual-authoring FALLBACK estimate is used (iron grate 70%, wood fence
gate 55%, wood window 55%, decorative wood panels 50–55%, dvergr 50%).
These numerical fallback porosities are NOT measured and must not
be mistaken for material constants or exact mesh measurements.
They are used only for verified shipped open-piece IDs.

Occlusion's material band energies are combined with that open fraction
and then converted to WA's existing two-pole gain/cutoff using
SpectralPaths.Decode. Proper source-volume occlusion is retained.
Room surface enclosure/mean free path/absorption/volume are weighted
by the same open fraction, so open screens do not close the room
acoustically to the same extent as continuous structural walls.

## Validity and limitations
- A coarse 2D projected union is an area approximation, not perfect
  orientation-dependent acoustic ray tracing through each slat.
- If visual geometry is represented only by an alpha mask on an opaque
  mesh, projected triangles can overcount opaque coverage.
- Fallback estimates cannot be called exact.
- The global current 3D occlusion still samples 16 rays by default,
  so transitions may be visibly discretized in some situations.
- Room probing still reads the first ray hit for enclosure; the
  per-panel weighted room model does not discover farther walls.
- WA's 2-band filter is not native Steam Audio 3-band parametric DSP.
- No in-game listening or CPU profiler sample was performed in this pass.
- No public release; review real log traces and sound quality first.

## Verification
Production net472 Release: 0 errors / 0 warnings.
Full prior regression suite: passed.
OpenPanelArea.cs production-linked synthetic unit checks: 8 passed
(opaque, half, slat, orientation, fallback, cache).
TransmissionEnergy + Loss from production source: 72 compiled tests,
including composite low+mid spectrum for 3 materials x 5 porosities.
7 ValheimSOURCE prefab inventories/dimensions verified.
Source inventory 47 and static release gate passed.
No added per-frame hooks, polling, coroutines or threads.

## Rollback
Close Valheim then execute:
backup\OPEN_PANEL_COMPOSITE_20261122\Restore.ps1
