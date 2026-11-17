# Wire's Shadows 0.1.12 — placed food and load-fade correction (2026-11-16)

STATUS: LOCAL TEST BUILD. NOT PUBLISHED. Installed in Enngard.
Compiler: 0 errors / 0 warnings. Menu startup recognized as version 0.1.12.
The user has NOT yet verified the exact visuals in a world.

## Reported regressions
- 0.1.11 claimed a feast/placed-food destruction shadow refresh improvement, but the user still observed a perceptible delay after breaking food.
- Shadows of newly loaded lights faded in over 1 second after world load/teleport. The user asked for immediate visibility.

## Traced paths in original source
- `Piece.OnDestroy` triggers `PieceGone` and `PieceChanged`.
- The same placed food is often an `ItemDrop` with `DropLifecycle.OnDisable/OnDestroy`, calling `DropRemoved/UnplaceDrop/OutOfCachedMaps`. This can duplicate the work and interrupt a partial face rebuild.
- `PieceChangedCore` previously returned early if no renderers remained, including during destruction. OnDestroy can fire after renderer disable, so that branch could suppress the invalidation entirely.
- `WakeRenders` starts its coroutine immediately. Called from a destruction callback, it may draw a cubemap before Unity has actually removed the object's renderers; the supposedly removed object is then captured for a while in a cached map.
- A completed partial map uses `DoorFaces`; 0.1.11 normally renders one face per frame with a ~2ms urgent limit. If a full map was dirty or staged, its priority could be lower than other work.
- `FinishMap` set `Fade=0` for every first map unless `Instant`; `Update` raised it over 1 second. A map arriving after the visible load was therefore faded.

## Implemented local changes
1. Identify placed `Feast` and piece-like `ItemDrop` on PieceGone, unify their DropRemoved and PieceChanged handling and avoid duplicate work. `d.Placed=false` on uncaching.
2. No render work inside a food-destruction callback. Reuse the existing next-frame wake. Unity renderers are gone by then. Other building changes keep their previous update path.
3. Always invalidate destroyed pieces by position when renderers have already disappeared; no early return on an empty destruction hierarchy.
4. Make placed item removal urgent even when it occurs through DropLifecycle without a PieceGone callback. Partial visible affected cubemap faces run at higher priority with up to ~12 ms of contiguous burst time (old 2ms budget retained for other edits). A dirty full map for a visible broken-food light completes together; an individual full-map refresh can cost more than the partial budget.
5. Loading/teleport map creation sets `SnapOnFirstMap` on the tracked light. When its map finishes, even after the loading window, set shadow fade to 1 immediately. Additional LightEnabled post-teleport path also respects this. Normal newly lit flames outside the load window keep original fading.
6. All existing Wire's Shadows 0.1.11 features remain: flicker culling (15m and offscreen), moving shadows, caches, existing fade for non-load new lights, tiled lights off, no indirect renderer.
7. No new Update/coroutines/polling. Reuses existing render scheduler and Update wake/fade.

## Validation limits
Compiled 0 errors / 0 warnings; Enngard plugin DLL, package DLL, and source build have identical SHA256.
Menu startup logged Wire's Shadows 0.1.12 without relevant loader errors.
Still requires **in-world** tests: break a feast under near lights while observing its shadow, remove another placed food item, consume final feast portions, load/teleport home into a large base, check any one-frame lag and measure frame-time cost. A next-frame redraw is visually immediate, not literally synchronous with the destruction callback; rendering before the destruction completes would risk stale shadows.
Test with timing log if there is still a delay; further changes should follow actual event/render logs.

## Rollback and archive
Backup source, original installed 0.1.11 DLL and old release documentation:
`backup/FOOD_IMMEDIATE_AND_LOAD_NO_FADE_20261116`
Rollback script:
`backup/FOOD_IMMEDIATE_AND_LOAD_NO_FADE_20261116/ROLLBACK_TO_0111.ps1`
Run with Valheim closed. It restores 0.1.11 source, package and Enngard binary, and rebuilds.

Public 0.1.11 remains available and unchanged.
