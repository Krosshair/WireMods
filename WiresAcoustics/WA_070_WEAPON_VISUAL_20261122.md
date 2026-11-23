# WA 0.7.0 — rendered weapon source volume (2026-11-22)
Status: installed to Enngard; unpublished release candidate. Gameplay listening and rain test still open.

## User feedback and actual cause
Previous one-shot event fix treated every action explicitly located at the hand as an acoustic point. A weapon whoosh is neither an arbitrary 30cm sphere nor the wielder's body collider: the air-moving weapon blade/head extends beyond the grip and follows its animation. In Valheim, VisEquipment exposes the displayed left/right item GameObjects (m_leftItemInstance and m_rightItemInstance) attached to their hand bones.

## Targeted implementation
- Spots.Place identifies only known Limb/swing/whoosh/claw-sweep audio in an actual currently equipped attack. WeaponVisual retrieves the real equipped visual model from VisEquipment. If it has a usable mesh extent, the event is moved to its visible local bounding-box center and attached/followed to that weapon visual (not the hand or whole character).
- Area.WeaponOf derives source extent from enabled MeshRenderer/MeshFilter sharedMesh.bounds and SkinnedMeshRenderer.localBounds, transforms all model-local corners into the weapon object's local box, and caches per weapon-visual instance ID. A collider-only weapon can fall back to its real collider bounds. Unusable or extremely large assets preserve existing hand-source behavior.
- Area.Measure uses cached WeaponShapes for WeaponSource events instead of body-collider shapes and skips generic static prop re-centering. HRTF size can include the longest weapon axis; source shape uses existing oriented source-volume ray samples. Does not determine power from collider/mesh size.
- Follow.Created preserves a weapon visual as the source owner rather than replacing it with the Character. Other known localized sounds remain points, including hand item operations, chest lids and impacts. Creature natural attack effects with no equipped visible weapon preserve their prior head/limb attribution.
- No changes to unrelated WS; no new per-frame components, coroutines, physics ray jobs or audio callbacks. Mesh collection happens only at first visual shape discovery and is cached.

## Model scope
The emitting area is a snapshot of the actual visible weapon mesh, updated as its bone/weapon transform moves. This is NOT a reconstructed full time-integrated sword-swing arc across every sample of an audio clip. Real Valheim model geometry and sound-event naming decide eligibility, so not all modded weapons can be guaranteed to match. Actual in-game directional impression and profiler overhead remain to be validated.

## Verification and artifacts
- net472 production build: 0 warnings, 0 errors.
- dev/TestWeaponVisualEmitter.ps1: source-linked current VisEquipment field, sound event selection, weapon shape, fallback, caching and source-owner preservation assertions PASS.
- dev/AreaInvalidationTests: extracted production Area.Invalidate tested for both physics and visual caches, ancestors/siblings and nested sound owners PASS.
- Existing 1281 VolumeSamplingTests plus 1604 Fresnel source tests PASS. Main nested suite and VerifyReleaseCandidate PASS, 51 C# production source files.
- Production DLL SHA256 B4936A3DB82827E9839CD3134C0555D503DBE470920C1525909C067A6780A973, copied to Enngard and package.
- Staged ZIP staging/WiresAcoustics-0.7.0-RC-weapon-volume-rain-pending.zip, SHA256 00D2BD2402DBD3D5FFE9B80DE4C38AE0B3326EDBE250D3FD67C0EF65BD347F82 (five root entries verified).
- Not published. Indoor rain runtime listening gate remains outstanding.

## Recovery
Pre-change source/test/package/deployed DLL backup is backup/WA_WEAPON_VOLUME_20261122. Rollback script Restore.ps1 requires Valheim closed.
