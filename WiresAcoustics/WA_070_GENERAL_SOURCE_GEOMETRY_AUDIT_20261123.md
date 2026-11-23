WA 0.7.0 RC - generalized owned source geometry
2026-11-23. INTERNAL ENGINEERING ONLY, not public changelog.

This follows earlier targeted chest and terrain placement fixes.

Root audit:
- Area.Measure previously defaulted parented source ownership to sound.root rather than the nearest meaningful source object.
- Sources could reuse AreaKnown/ShapeHalf/AreaRadius across playback incarnations if not invalidated.
- SourceVolume.Count sized the shape-ray budget from arbitrary spawn pivot instead of projected actual owner center.
- Only chest events used listener-facing exterior shell while other owned physical emitters sampled inside full solid-body volume.

Corrections:
- Resolve nearest connected ZNetView, Character, Piece, Container, Fireplace, Destructible or Rigidbody, preserving explicit ShapeRoot overrides.
- When a new playback starts, invalidate stored source bounds and measure fresh. Clear cached center, half extents and radius before a failed/rebuilt measurement.
- General SurfaceDomain: an owned, nonlocalized, nonfire and nonweapon sound emits from real listener-facing oriented collider surfaces. Exact-localized points retain point origin, fire keeps flame, weapon keeps swept visual mesh. The shell uses the existing same 4-32 volumetric rays, no extra budget.
- Count sampled source rays using projected shape center, not possibly offset prefab pivot.
- Debug logging identifies localized-point, physical-surface, volume, weapon, fire, source owner; only when enabled.
- No new per-frame loops, coroutines, synthesized audio or weather sources.

Regression:
Full VerifyEveryImplementation.ps1 PASS; source domain 16 classification tests + volume near-pivot displacement test and all prior suites.
Build: 0 errors, 0 warnings.
Installed Enngard DLL SHA256: 00B3A4B4DF045BCB83A95F2CD6E6969C8B23B01C277AF61A621FAC937C73B085.
Rollback prior Enngard DLL: C:\Users\kajet\WireMods\WiresAcoustics\backup\GENERAL_SOURCE_GEOMETRY_20261123\installed-WiresAcoustics.dll
Staged internal candidate: C:\Users\kajet\WireMods\WiresAcoustics\staging\WiresAcoustics-0.7.0-RC-general-source-geometry-20261123.zip

Known remaining limits:
- Sources that have no owner/explicit authorial origin still default to points. Guessing nearest object is intentionally refused in ambiguous dense scenes.
- The complete game audio playback/perceptual placement matrix and native audio-thread profiler have not been run against this candidate.
- This broad emission-domain adjustment may change perceived balance; in-game validation required before 0.7.0 public release.
- Some real impact point sources must remain localized even if within an owned physical object.
- A nested source under a larger shared network hierarchy may need per-collider ownership rather than simple root-level skip; no speculative change to that logic in this candidate.

Do not publish directly until listening and performance validation.