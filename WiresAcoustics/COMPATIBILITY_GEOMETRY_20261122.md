# Wire's Acoustics 0.7.0 — geometry change compatibility, 2026-11-22

LOCAL TEST ONLY. No public release. No in-game QA yet.

## Correctness audit
The previous test revision removed the 1-second stationary room raycast and 2-second unchanged-loop fallback entirely. This improves idle CPU but causes a known omission: if another mod modifies geometry without invoking Piece/Door hooks, the stored acoustic geometry remains stale indefinitely until the listener or source moves. A universal event-only solution cannot observe arbitrary third-party Physics/collider mutations without either patches for those producers or independent validation. The previous removal is not acceptable as a zero-regression replacement.

## Implemented in this revision
- Restore the original 1-second stationary room geometry safety refresh, through the existing worker-job path.
- Restore 2-second loop safety rechecks, with the event/motion fast-path preserved.
- Add TerrainComp.ApplyOperation Postfix, invalidating acoustic geometry for terrain edits. Since a terrain chunk's origin is not the actual edit site, invalidate all cached paths rather than assuming the chunk origin lies within the changed path.
- Add MineRock5.RPC_SetAreaHealth Postfix, detecting per-area mining changes that do not destroy the overall object, with broad invalidation to avoid missing distant sub-regions.
- Add Destructible.DestroyNow Prefix, detecting scenery destruction beyond Piece destruction.
- Reuse GeometryEvents.Changed and Occlusion.WorldChanged to enqueue affected sounds. No new per-frame update, coroutine, physics polling, or worker scheduling system.
- Keep the continuous edge/ambience/underwater work from the previous installed development build untouched.

## Boundaries
- Existing unknown-source changes still require the conservative safety refresh. Setting its interval to infinity or removing it is incorrect unless external change coverage can be demonstrated.
- Additive Harmony patches are statically compiled but haven't been validated with the Valheim runtime and full mod stack.
- No measured CPU or audible regression data. Back out if any runtime patch exception or noticeable frame spike appears.
- No proactive full-world acoustic-geometry cache has been implemented in this pass. Previously unseen wall edges are still discovered by batched raycasts after source obstruction.
- Restoring the safety refresh means periodic worker raycasts remain while idle. This preserves correctness relative to the original behavior, but does not achieve an absolute zero-idle-scan requirement.

## Rollback
Quit Valheim. Run backup\EVENT_DRIVEN_SCANNING_20261121\Restore.ps1.
