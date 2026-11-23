# Wire's Acoustics 0.7.0 — portal connectivity topology audit
Date: 2026-11-22. Local development only, not published.

## Independent comparison with established acoustic systems
- Wwise Spatial Audio room-portal propagation treats rooms and portals as
  connected acoustic structures, not directed scan-order components:
  https://documentation.help/Wwise-Unity/pg__rooms__portals__tut.html
- Steam Audio's acoustic probe pathing uses mutual visibility and can
  validate routes when dynamic scene geometry changes:
  https://valvesoftware.github.io/steam-audio/doc/capi/simulation.html

WA still only infers candidate openings from 64 room rays; unlike Wwise it
does not have explicit AkRoom/AkPortal objects or guarantee room connectivity.

## Verified defect
Portals.BuildNeighbours chose 6 closest directions independently for each ray.
The relation need not be symmetric: A can list B as a neighbour while B does
not list A. Its flood-fill Find was based on this DIRECTED graph, so the
connected components of a candidate acoustic opening could depend on the
ray's enumeration order. A group of physical escape rays is an undirected
connected region, and must not depend on which ray is visited first.

## Implementation
Portals.BuildNeighbours now constructs a symmetrized adjacency union:
link[i,j] == link[j,i] if either ray selects the other as one of the nearest
6 directions. The graph is created only once, and reused for subsequent room
scans. Ties have a deterministic index fallback. No extra raycasts,
scene inspections, per-frame Update, coroutine or polling introduced.

The actual portal destinations still come from measured escaped room rays;
the change only corrects grouping of pre-existing ray results.

## Immediately performed independent validation
- Net472 project built without warnings/errors after the source change.
- dev/PortalTopologyTests compiles ACTUAL production Portals.cs with
  deterministic Unity/Room stubs. It independently rebuilds graph adjacency,
  checks all edges symmetric and non-self, and checks that Find groups are
  invariant under 600 permutations of the 64 room directions across
  40 simulated escape masks. All passed.
- dev/VerifyEveryImplementation.ps1 extended to run PortalTopologyTests.
- Full suite rerun and passed: 354 digital DSP tests; 38 diffraction parity;
  324 Fresnel geometry/material cases; 400 corner propagation cases; 75 finite
  obstacle geometries; 100000 packet concurrency meter; source geometry
  cache; environmental and acoustic trace tests.

## Limitations, not hidden
- Symmetrizing k-nearest-neighbour graphs may join close sampled exit
  regions that represent two distinct physical apertures. A true portal
  requires verified opening topology, not just graph symmetry. Actual
  Valheim gameplay/geometry tests are still needed to quantify that risk.
- Same 64 ray resolution and largest-four-candidate cap.
- No explicit connectivity graph between multiple rooms.
- No impulse-response reverb, measured HRTF or full 3D multi-edge solver.
- No verified in-game CPU or audible behavior. Tests are synthetic/structural.
- Technical correctness within this graph property does not imply
  parity with Steam Audio's pathing or Wwise's full room-portal solver.

## Rollback
Close Valheim and run backup\PORTAL_TOPOLOGY_20261122\Restore.ps1.
