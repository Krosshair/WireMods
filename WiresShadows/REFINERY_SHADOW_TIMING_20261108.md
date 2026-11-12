# Eitr refinery: source-light versus moving casters and delayed geometry shadow edits

## Root analysis
- The refinery's light is not registered via MovingLight() (which is reserved for carried, vehicle, rolling and preview cases). Regular point lights on static Smelter/Artisan/etc pieces are handled by Register(), stationary cached map. This already matches the user's desired rule.
- But Machines.cs registers Smelter's animator hierarchy's renderers as DYNAMIC CASTERS, hides these renderers from every stationary cached shadow map, and forces moving-shadow group hash = Time.frameCount whenever Smelter.SetAnimation(active) says the machine is operating, plus MachineSettle=2.5 seconds after it stops. The boolean is an operation/animation parameter, not proof that the mesh transforms move. Wastes dynamic face renders and can create visually late/odd caster changes.
- Iron bars disappearing close to refinery historically caused delayed shadow update. Previous PieceChanged always queued SIX static cubemap faces PER light, regardless of which direction the changed piece occupies. A highly lit room causes long serial GPU queues even for a local 1-face change.
- The static light source itself is NOT the moving problem. Never change native moving-light classification of real moving lights, and never disable moving shadows for people or real animated machine parts.

## Code changes in CORRECT src_base
1. src_base/Machines.cs RegisterMachine(Smelter): eitrrefinery-by-prefab-name exception: do NOT register its Smelter animator hierarchy as moving casters. Its shell thus participates as ordinary static geometry in FindCasters, illuminated and occluded by static cached lights. We are acting on the user's direct observation that the Eitr refinery geometry is stationary. The Spinning Wheel and Windmill remain allowed as truly moving caster geometry; Artisan table is not a Smelter and was not added as a moving light. No light classification change.
2. src_base/ShadowCache.cs PieceChanged: while a light has a complete map and no full refresh in progress, calculate conservative bounding-sphere FacesFor from actual changed piece renderer bounds (or 1m fallback at provided position), OR those bits into the PRE-EXISTING DoorFaces queue, mark DoorPrepared=false. The already-established RenderDoorFace path updates only affected cube directions at its existing one-face-per-frame budget and refreshes caster collection. This is a targeted solution for delayed shadow appear/disappear after iron bars change; it reduces N*6 faces to only affected faces.
3. Unready lights or those mid full-map render retain existing whole-map Dirty scheduling and no-abort/no-extend behavior; nearby light source map does not become prematurely invalid.
4. Existing global non-vanilla light self-exclusion remains exactly as is. No shader changes, no new per-frame hooks, no timers or periodic method additions. The existing RenderDirty coroutine is reused.

## Validation/status
- Built corrected src_base Release: 0 warnings, 0 errors.
- Installed in Enngard while game closed. Installed DLL SHA256 DAAD671A66D2FD4A33F0F5DAA2CF2142B7CE85CCA2DCA7425AB38B06911E20C0.
- Shader bundle untouched, SHA256 65E65AD40B30529A733139EC01629148326C2FB59F3C63EA66262B422EB311DC.
- Backups of previous source, DLL, shader under backup/refinery_static_light_fast_pieces_20261108, Restore.cmd restores previous DLL.
- **Not visually validated**. Must confirm with an iron bar being placed/removed beside the active Eitr refinery, and check static light renders correctly with a windmill/spinning wheel still casting genuinely moving geometry.
- The refinery-specific exception uses the engine prefab name eitrrefinery. If the actual renderer has geometric motion from an Animator, its moving shadow will instead be frozen by this optimization; revert this exception if user observes a moving refinery model. It does not impact the point light's own movement status (already stationary).
- Optional later work: implement general actual-transform/mesh pose detection for machinery casters instead of relying on Smelter active flag. No extra per-frame probes were introduced in this patch; don't promise generic classification is solved by this narrow exception.
