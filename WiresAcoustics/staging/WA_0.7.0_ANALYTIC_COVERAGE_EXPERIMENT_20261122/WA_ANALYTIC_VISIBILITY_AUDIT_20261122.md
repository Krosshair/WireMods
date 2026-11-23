# WA 0.7.0 — analytic direct-source visibility prototype
Private development candidate. 2026-11-22.

## Motivation / observed sound
User reported repeated all-or-nothing obstruction, even with partial
transmission, after 0.7.0 material and performance corrections.
Source audit confirmed source-volume ray hits and center/side three-ray
occlusion are DISCRETE. A low-pass or volume glide only smooths a
sampled state transition after the fact; it cannot make the underlying
geometric visibility continuous.

## Actual implementation (NOT a complete replacement of propagation)
New production: src\AnalyticOcclusion.cs
AnalyticOcclusion.DiskRectangle() uses a closed-form unit-circle /
axis-aligned-rectangle overlap integral. No ray density dependence.
For a measured single BoxCollider acoustic wall, BoxCoverage
projects a sphere-source cross-section onto the collider's estimated
front face, using actual collider rotation, bounds, and nonuniform scale.
This yields a continuous obstruction share as the player moves in a
fixed source/collider configuration.

For known Valheim open panels, OpenPanelArea.cs remains a fixed exact
prefab-name property lookup; no runtime mesh scans/triangulation. The
acoustic material transmission coefficients remain fixed Valheim
WearNTear -> Steam Audio preset properties.

Spectral energy blend:
 low = 1 - covered * (1 - wallLowEnergy)
 mid = 1 - covered * (1 - wallMidEnergy)
SpectralPaths.Decode fits that to WA's existing TWO-band DSP, which
retains the known 8k+ limitation.

Existing physics raycasts still discover candidate wall colliders and
independent portal/corner paths. Occlusion.Block.FirstBox retains a
verified BoxCollider wall candidate. Batch direct spectrum uses
analytic area instead of blocked ray counts on single-wall, no-terrain
no-soft cases. Existing independently measured indirect paths remain,
scaled by sqrt(covered) as its known geometric shadow share. The
ongoing Directions.Step callback recalculates the direct and indirect
energy on EVERY listener update using cached wall geometry, without
any additional physics raycasts or new MonoBehaviour hooks.

Starts.Step also uses the analytical model for newly held sounds, to
avoid a start-then-recheck hard switch. Open-panel coarse colliders
can be discovered from source-volume ray hits even though they have no
solid-wall corner-edge record.

The face normal is selected by largest projected box-face area to
handle narrow iron grate / fence bars correctly when they have two
narrow dimensions. This selection IS DISCRETE at equal scores; box
face switching may still cause a geometric jump for some rotated
objects. Future general convex-hull silhouette projection could remove
that approximation.

## Real limitations (important)
- This does NOT provide provably step-free audio in ALL scenes.
  The existing raycast system discovers candidate walls using discrete
  samples; gaining/losing a candidate can still introduce a step.
- Multiwall paths, soft obstacles, ground/terrain and transitions among
  several physically measured opening/corner routes are still handled
  by the existing finite-ray geometric solver.
- Source radius/box-front-plane are APPROXIMATIONS. The disk/rectangle
  overlap is mathematically exact only for that planar projection.
  The complete 3D wave field is not solved.
- Box face selection is a discrete design branch that can change
  if listener/source orientation changes enough; an exact projected
  box silhouette is preferable long-term.
- The existing two-band DSP, time discretization and source/ray
  scheduling remain. No promise of zero audible steps before game QA.
- Continuous area calculation adds CPU arithmetic to existing
  Directions.Step for wall-modelled sources. Main-thread benchmark,
  frame-time p95/p99 and sound listening have NOT been done.
- Saturated max-hit ray buffers remain a separate issue in dense
  Valheim builds and may affect first wall discovery.

## Offline verification
dev\AnalyticOcclusionTests directly compiles and calls the actual
production AnalyticOcclusion.cs with deterministic Unity stubs:
- full/half/quarter/tangent/stripe disk-rectangle area
- 1000 small geometric movement increments, monotonic without
  discrete coverage jumps
- rotated/scaled wall and thin iron bar
- source and authored-material energy consistency
- continuous weighted indirect energy, geometry invalidation.
This is a NUMERIC MODEL TEST, not in-game listening.

dev\VerifyReleaseCandidate.ps1 includes the new file in inventory,
checks source integration in Batch, Starts and Directions and runs the
full existing regression suite. Static gates do not certify fidelity.

## Rollback
Close Valheim and run:
backup\ANALYTIC_VISIBILITY_20261122\Restore.ps1
The original 47-file source and exact pre-change DLL are preserved.
