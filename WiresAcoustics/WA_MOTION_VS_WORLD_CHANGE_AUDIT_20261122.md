# Wire's Acoustics 0.7.0 - movement vs world-change transitions
Private development update, 2026-11-22.

## User audio goal
Perceptually convincing Valheim sound, not ray visibility masquerading
as propagation. Slight occlusion should produce mild color/level change
when edge diffraction/open paths dominate; moving behind solid geometry
should gradually change the spectral balance. Placing or deleting a
wall, operating a door and changing terrain should react promptly.

## Confirmed bug and targeted correction
Occlusion.WatchLoop queued acoustic checks after listener/source motion
and set FastGeometryResponse unconditionally. AcousticFilter.Set consumes
that flag to select one-buffer DSP transitions. This made ANY movement
check essentially instantaneous, obscuring normal temporal smoothing.

AnalyticOcclusion.Refresh and ContinuousEdges.Evaluate did the same
on purely geometric movement. Those three sites now update their
DSP targets using the ordinary smoothing mechanism.

Actual world edits still set FastGeometryResponse in Batch.GeometryChanged,
and newly started sources initialize immediately. No new polling,
Update, LateUpdate, FixedUpdate, physics rays, coroutines or threads.

## Important remaining design work
The 0.7.0 solver still uses discrete source-volume rays to discover and
rank paths. Analytic disk/rectangle coverage applies only to a single
BoxCollider wall and is not a general wave diffraction solution. The
single-edge Fresnel model is restricted to specific validated shapes.
A fully occluded path and an opening path can be selected in distinct
batches; fading their result is not sufficient to avoid perceptual
state changes. The proper next work is to calculate a shared continuous
frequency-dependent energy response from direct transmission plus
diffraction around nearest valid edges plus opening paths, with
Valheim-authored obstacle characteristics and perceptual tuning. Near
a grazing edge the actual wave/path geometry should dominate, NOT
a fabricated minimum-volume floor or binary wall flag.

A full general propagation redesign has NOT been implemented here.
The actual in-game audible result needs listener verification.
CPU profiling is not available from the static suite.

## Verification
Compiler 0 errors, 0 warnings.
New targeted static test: dev\TestMotionVsWorldChange.ps1
Complete dev\VerifyReleaseCandidate.ps1 regression suite: passed.
Restore when Valheim is closed:
backup\MOTION_VS_WORLD_CHANGES_20261122\Restore.ps1

No public release.
