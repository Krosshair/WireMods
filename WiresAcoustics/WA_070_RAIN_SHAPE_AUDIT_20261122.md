# WA 0.7.0 — Rain Response and Source Geometry Release Candidate
Date: 2026-11-22
Status: DEVELOPMENT ONLY — automatic tests run; actual in-game rain/geometry listening required before publishing.

## Root causes
1. Room.Measure caches 64 rays and does not recast unless moving >18 cm or geometry invalidation occurs. The old code applied exactly one Mathf.Lerp to Enclosure, BuiltRoof, and WallBed per new ray measurement. When a new room is stable and listener stationary, that partial value can remain indefinitely; it is not an actual temporal fade.
2. WeatherAcoustics had a 1-second clip-recognition blend and used s_inside/s_gain, which also took up to one second to move even when room geometry changed. A delayed/attenuated weather-bed response is therefore expected with the prior implementation.
3. SourceVolume.Project used the cached Area collider box merely to compute the enclosing sphere radius. The 4/8/16/32 finite ray sample points lay in that sphere, which can extend well beyond narrow actual machine, door, or hearth bounds.

## Corrections
- Room.Measure now commits the measured geometry Enclosure, BuiltRoof, and WallBed on each completed worker measurement (not a one-time Lerp). The already-existing smooth s_current reverb tail and the general s_inside ambience transition remain unchanged.
- WeatherAcoustics.RainResponseSeconds = .25 s. The weather-identity blend and a dedicated s_rainInside smoothly approach the actual roof/enclosure target within 250 ms. The rain weather bed uses its own fast target gain/cutoff from current measured WallBed. General wind/ambience and room reverb remain at the prior slower rates.
- SourceVolume.Project retains the same MaxSamples and angular-extent sample quality heuristic. For known looping source-owner geometry, it reuses Area.ShapeHalf/ShapeCenter/ShapeRoot local oriented bounds and samples deterministic Halton lattice points INSIDE the actual transformed box (no additional casts). Unshaped/short one-shot effects continue using configurable 0.30-m sphere. Sample count and ray scheduling are unchanged. No new Update/LateUpdate/FixedUpdate/coroutines or polling.

## Tests
- net472 Release build: 0 warnings/errors.
- dev/VolumeSamplingTests tests rotated 0/45/90/180-degree owners and scales .5/1/2, checks every sampled point after inverse transforming to owner-local box; sphere fallback and Fresnel continuity checks.
- dev/WeatherAcousticsTests tests exact active rain loop, no coloration of birds, continuous 250ms entry and exit, roof interpolation.
- dev/TestRainAndShapeRelease.ps1 checks production source instrumentation, room measurement direct assignment, fast rain and retained general/reverb fades, box sample geometry and fallback.
- VerifyReleaseCandidate.ps1 full nested integration tests across 51 production C# files: final result in RELEASE_CANDIDATE_VERIFICATION_20261122.txt.
- Production DLL SHA256: 653BC79DD75927F92C117D9D38690133B93288989D8F9D348EDC4D47FF302F2B. Installed into Enngard, and matching DLL in package.
- RC ZIP: staging/WiresAcoustics-0.7.0-RC-rain-geometry-20261122.zip
- RC ZIP SHA256: 874F34AA268F9E31FD9A457454D642A2A99568A9C07036A11919F502FE0152A6. Five required root entries verified.
- In-game acoustics and combined mod CPU remain UNVERIFIED.

## WS profiler, separately
Latest 2026-11-22 Enngard captures:
- 21:20:30: WS 0.728 ms/frame, WA 0.334 ms/frame, ~61.6 fps
- 21:25:04: WS 4.508 ms/frame, DrawFaceCore 2.692 ms/frame, WA 0.198 ms/frame, ~51.4 fps
- 21:19:11: WS 6.165 ms/frame, DrawFaceCore 4.313 ms/frame, ~47.3 fps
- WS internal log around 21:25: 120 cached faces/255 frames, 2.688 ms/frame in cubemap rendering plus 21 caster searches and 48 partial-face redraws; placement/food events present.
- There is also a 46.96 ms cached-face worst individual frame and a separate 199.05 ms peak during 3465 world pieces streaming in. Do not equate those with normal stationary performance.
- The expensive primitive is Unity Camera.RenderToCubemap inside ShadowCache.DrawFaceCore. It is driven by face redraw/rebuild count; ordinary slot ranking/transfer is not the primary cost. No WS source or settings changed.
- No GPU frame timings. Cross-location profiler captures are not a controlled before/after comparison; profiling itself adds overhead. For next WS test use identical position/camera angle after caches have settled, compare WS enabled vs disabled or controlled view scenes; investigate redundant invalidations before simplifying maps.

## Rollback
backup/WA_RAIN_SHAPE_20261122/Restore.ps1 — Valheim must be stopped, restores source/tests/package/deployed DLL.
No public release was performed.
