# Acoustic geometry scans: motion/events only — 2026-11-21

Local test patch for Wire's Acoustics 0.7.0, built on the continuous-acoustics revision.

## Existing mechanism reused
- New sounds: Starts.Schedule submits initial direct/face raycasts to worker jobs; starts playback after result.
- Active sources: Occlusion.WatchLoop is driven by existing Directions positional updates, with movement thresholds and minimum spacing.
- Build/change: GeometryEvents emits Piece.Awake, Piece.OnDestroy and Door.SetState invalidations; Batch.GeometryChanged enqueues affected live sounds.
- Room geometry: the existing 64-direction room-ray job runs on worker threads after listener motion or geometry changes.

## Changed in this patch
- Room no longer schedules a once-per-second safety raycast while listener and room geometry are unchanged.
- Occlusion.Unchanged no longer expires solely because two seconds have elapsed. Active looping sounds without movement or geometry edits no longer require periodic safety raycasts.
- Movement and explicit building/door events still request acoustic propagation via the preexisting job queues. The existing per-frame positional update remains; no additional hook or polling loop was added.
- Kept conservative 8 cm source/listener threshold near uncached walls, 12 cm clear, 25 cm with a verified continuous edge model. These are *revalidation thresholds*, not steps in audible muffling.

## Limitations
- This does NOT implement a complete shared world-geometry cache or pre-discover unknown walls. Starts still does initial sound direct/face raycasts and Batch/Corners still discover alternative paths after first obstruction.
- Previously untracked geometry changes bypassing game hooks (terrain modifications from other mods, physics changes to non-door objects, etc.) may now remain stale until listener/source movement or another recognized world event.
- Bookkeeping runs on the existing audio/game thread; Unity raycast commands execute via worker jobs, but creating jobs/results and Unity object access still cost main-thread CPU.
- No actual in-game audio/CPU profiling completed. Release only after survival-mode validation.

## Tests
1. Stand completely still next to a steady smelter: no repeated room/loop raycasts after outstanding jobs have completed.
2. Walk 8+ cm near a wall: batch scans resume. Hold still: jobs settle again.
3. Place/remove a wall or toggle a door without moving: affected sound rechecks and room invalidation happen.
4. Walk back and forth across wall edge; listen for discontinuities and watch profiler.
5. Check a moved sound source while listener remains still; rechecks should respond to source movement.

## Rollback
Close Valheim. Execute backup\EVENT_DRIVEN_SCANNING_20261121\Restore.ps1 in PowerShell to restore the source files and previous installed/package DLL.
