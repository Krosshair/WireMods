# FAILED / REVERTED (2026-11-16)
This is a historical record of the unsuccessful 0.5.12 prototype. The user tested it and reported that the changes DO NOT WORK. Active source, package and Enngard DLL were restored to 0.5.11. See ACOUSTICS_0512_FAILED_REVERTED_20261116.md.

# Wire's Acoustics 0.5.12 — local test build (2026-11-16)

## User intent
1. Smelter ore/fuel interaction sounds come from the operated part, without moving ordinary machinery sounds.
2. If any sampled point on the actual sound-source bounding box has a clear acoustic path to the listener, do not muffle or redirect the sound. Its perceived position remains the authored source location. The source box is not a new virtual sound position.
3. Preserve existing 0.5 ms main-thread work budget, three-ray anti-seam checks, precedence/routing rules when genuinely blocked, accessibility and HRTF behavior.

## Changes
- `src/Spots.cs` detects Smelter.m_oreAddedEffects and m_fuelAddedEffects by exact EffectList reference, chooses m_addOreSwitch or m_addWoodSwitch world transform (no string-name heuristics), and relocates only the associated ZSFX. Both local and networked EffectList.Create cases are covered where their sound is attached to its owning smelter or the root can be found at the effect position.
- `src/Filter.cs` stores `PartInteraction` metadata; `src/Area.cs` treats that sound's bounds as a small volume around the switch, rather than the full unrelated machine. Other sounds retain their original area classification and fire sizing.
- `Area.FacePoint` supplies 14 conservative world AABB surface samples (6 face centers and 8 inward-offset corners). The sound is NEVER relocated to these samples.
- `src/Batch.cs` schedules the surface lines as part of the existing Physics.RaycastCommand worker batch, with three parallel rays per line and the unchanged occlusion evaluation. They are only requested if a box source is blocked or being confirmed after it becomes blocked. One unobstructed three-ray group removes obstruction and the route for that sound, retaining the original source point, distance, HRTF and reverb calculations.
- Actual sample endpoints are frozen with the queued ray commands, so source rotation/motion/destruction after submission cannot cause mismatched evaluation.
- A newly blocked box whose last result was clear gets a regular next-batch continuation; short one-shot sounds check up to six face centers synchronously only while inside the existing CPU budget.
- No new Update, LateUpdate, FixedUpdate, coroutine or polling hook. Diagnostics show number of additional sample lines and sources cleared by their bounds.
- The source/filer visibility decision is conservative across 14 samples, not a mathematical proof for every infinitely many points on a 3D bounding surface.

## Verification
- Release build completed with 0 errors/warnings; installed to Enngard, package manifest/docs updated to 0.5.12.
- Enngard startup of 0.5.12 verified; no related exceptions observed. Final end-point-buffer build compiled and was installed afterward.
- In-world listening tests (smelter ore/fuel, partially exposed machine around walls, completely blocked sound, wall seams) still require gameplay verification.
- This is NOT published to Nexus/Thunderstore/Hexium/GitHub yet; awaits the user's confirmation. Public 0.5.11 remains unchanged.

## Rollback
Close Valheim and run `backup/SMELTER_BOUNDS_20261116/ROLLBACK_0512.ps1`. It restores the original exact source and installed 0.5.11 plugin and package files. The original DLL hashes are retained in the backup folder.

## Follow-up corrections, November 16
- Reviewed the actual Valheim Container.SetInUse, Door.SetState, Smelter.RPC_AddOre/AddFuel, Fermenter.RPC_AddItem/Tap, CookingStation.RPC_AddFuel, ShieldGenerator.RPC_AddFuel, Incinerator.RPC_RequestIncinerate, Fireplace.RPC fuel methods, and ArmorStand interactions. These methods supply event-time component ownership when an effect is created at a pivot with no parent.
- Fixed main weakness of 0.5.12 initial implementation: chest closing lost its owner when created with baseParent=null, so its bounds were not reliably evaluated. Follow/Spots now carry explicit owner to every generated ZSFX filter; nested audio subobjects are handled.
- Six immediate budgeted face samples for short blocked one-shots; 14 samples in async workers for remaining obscured source boxes. Existing source position/HRTF unchanged.
- Not yet tested physically in a Valheim world; validate chest close with partial open top, full wall behind, smelter ore/fuel, fermenter, and large box audibility.

