# Wire's Acoustics 0.7.0 — General environmental emitter fields
Updated: 2026-11-23
State: architecture study / source verification ONLY; no changes to DLL or live production C#.
This supersedes WA_070_VIRTUAL_RAIN_SOURCE_DESIGN_20261123.md (single POINT concept) and all earlier rain exposure mixers.

## User's architectural requirement
Environmental sounds should be represented as actually distributed emitting geometry. For rain that is *any surface exposed to rain* (terrain, roofs, rocks, outdoor floors, water), not a window, direction, one arbitrary point, or the player's local sky visibility. The same abstraction should cover other ambience: winds through vegetation, waves along coasts, insect beds in vegetation, etc., using appropriate source domains for each recording. Feed that geometry through WA's ordinary acoustic path and aggregate once into the game's existing sound playback.

## What Valheim actually exposes (locally inspected source)
- ValheimSOURCE/csharp/assembly_valheim/EnvSetup.cs: m_isWet, m_psystems, m_psystemsOutsideOnly, m_ambientLoop, m_ambientVol.
- ValheimSOURCE/csharp/assembly_valheim/EnvMan.cs: SetParticleArrayEnabled toggles ParticleSystem.emission, Clear/Simulate/Play. Its environment handler switches m_psystems, sometimes based on Player.InShelter(), and queues AudioMan.QueueAmbientLoop(env.m_ambientLoop,env.m_ambientVol). There is **no exposed surface impact-position array** in this orchestration code.
- ValheimSOURCE/csharp/assembly_valheim/WrapParticles.cs moves/wraps visual particle positions around a region; it does not identify physical rain-impact surfaces or imply a ready-made audio emitter field.
- Terrain height is available from Valheim's cached Heightmap geometry. Built/roof collider information is cached in WA Room.Solids; roof class currently identifies authored roof prefabs by name. Acoustic paths and multiple portal legs exist in WA Batch/Occlusion/Portals, with material-dependent low/high-frequency transfer.
- These observations do not conclusively rule out particle collision data in *individual serialized particle prefabs*, which have not been comprehensively inspected. It would be unsafe to assume actual rain collision positions are available just because rain is visually rendered.

## One general abstraction, no rain-specific acoustics
EnvironmentalEmitterField:
  SourceDomain: Point | Surface | Volume | Diffuse (depending on actual sound)
  SurfaceProvider: terrain, cached meshes/Collider geometry, water surface, vegetation, moving machines, weather-exposed building surfaces
  PhysicalEmitterDensity: energy/area or energy/volume and authored sound type
  ActiveCondition: weather, biome, time of day, local environmental settings
  Material/Spectrum: source material characteristics when available; otherwise use the game's authored recording as the spectral baseline
  SpatialPatches: cached stable world-space sample positions, coverage area and geometry/exposure IDs

A patch is not an additional AudioSource. It is a point/area SAMPLE of one real distributed emitter domain for solving acoustic propagation, not a new sound recording or fake single point of rain.

## Generic pipeline
1. Determine ACTIVE sound and source domain from existing Valheim AudioMan/EnvMan and physical game objects. Rain: weather-exposed surface patches with intensity from selected weather. Other ambience registers a different domain (vegetation, water, air, localized birds, etc.).
2. Build/cache a bounded, spatially representative field of world-anchored surface patches around relevant listener acoustics. Surface area weights depend on represented surface, not arbitrary sample count. Cache by world geometry generation, roof/door updates, terrain changes, listener region. Surface exposure to rain = upward precipitation unobstructed at that *surface*, not listener-facing sky.
3. For each relevant emitter patch, solve the ordinary listener->emitter acoustic path through WA's existing material, terrain, wall, diffraction, portal/opening logic. A window is a propagation PATH, NEVER a source or heuristic volume gate.
4. Aggregate the patch *received acoustic energies*: e.g. low-band E_L = sum(areaWeight_i * sourceDensity_i * gainLow_i^2), and high-band E_H similarly with high-frequency path transfer. Normalize both against calibrated outdoor unobstructed field to obtain one bounded gain/cutoff, using WA's existing spectral energy decoder.
5. Apply that one combined transfer to the original game's ambience AudioSource through its existing AcousticFilter. Do not spawn or phase-offset copies, create bridge voices, artificially move the AudioSource, or process a copy of the clip. Smooth the transfer, not the source activation.
6. If the sound's spatial position matters later, an energy-weighted arrival direction can be derived, but multiple independently moving audio voices are NOT a prerequisite.

## Key physical consequences
- A listener inside a very open hall hears the substantial outdoor terrain field through the open arches, regardless of camera facing; geometry/path energy determines loudness and brightness.
- A basement has exterior field paths filtered by basement floors/walls/rock. An attic receives closer roof-surface impacts if the actual outer roof is exposed, no special roof-loudness trick required.
- Closing a window affects the acoustic path. It does not deactivate rain or move the physical emitter to a different doorway.
- A few rays missing the opening cannot erase the whole source field: multiple fixed world-space patches contribute, with stable area weights and hysteresis around patch selection.
- Outdoors receives the authored game's normal rain volume (normalized reference). When moving between equivalent exposure positions, sample count changes must not affect loudness.

## Engineering constraints
- Current WA Batch.Enqueue and lifecycle assume f.transform is the actual SFX emitter, and deliberately exclude Ambient and Outside. DO NOT enqueue the original 2D ambience filter as a normal physical SFX without refactoring.
- Factor existing Batch/Occlusion solver to accept a source position/projection and produce spectral transfer as pure result; preserve existing portal/corner route handling. Aggregate results by ambient loop ID; a separate field geometry does not require multiple AudioSource instances.
- Limit sample budget (adaptive / importance- and distance-weighted), reuse existing async physics job, never introduce a new per-frame Update/LateUpdate/FixedUpdate, coroutine or polling loop; and avoid per-frame native allocations or scanning world objects. Recompute only when listener moves far enough, relevant geometry/weather changes, or emitter field invalidates. Profile separately to prove overhead.
- Material-specific *timbre* cannot be synthesized from Valheim's one rain clip simply by moving/filtering it. The architecture can reflect material-dependent spectral transfer; convincingly distinct water/wood/metal impact sounds require suitable source assets or DSP later, not another acoustic source model.
- Do not map every environmental recording to terrain: wind-in-reeds -> plant domain, surf -> shoreline/water domain, birds -> localized/diffuse biological sources, rain -> exposed surface domain, wind weather noise may be an atmospheric volume domain.

## Retire earlier experimental rain code during implementation
Delete RainField (including 104 added rainy room rays), RainField.Access/CombineAccess/GroundAccess and cached aperture volume gates, WeatherAcoustics arbitrary roof and indirect gain floor, and special-case Ambience rain mixing. Do not touch the verified non-rain wind, biome ambience, sound effect routing, or WS.
Keep original EnvMan->AudioMan weather playback unchanged.

## Suggested delivery sequence and test gates
1. First implement field-domain / patch weighting and pure spectral aggregation with deterministic non-game tests, preserving the currently installed build until it is viable.
2. Prove field sampling and normalization for an outdoor flat plain, 2 large arches, oblique window, fully enclosed basement, attic under real roof, rain on water, and roof above basement. No arbitrary rain-specific volume rules.
3. Extract/adapter existing Batch acoustic path for one virtual sample; compare its low/high gain and portal result against an identical actual 3D SFX at that location. Critical to avoid silently duplicating physics with a weaker custom routine.
4. Add bounded multi-patch processing and apply aggregated gain/cutoff to one original ambient loop. Test transitions on door edits, player motion, and clip changes with 0 new ambience voice duplicates.
5. Run comparable WA profiler captures (dry/rain, open hall/sealed building, near/far and busy base). Verify native physics work too. Rollback-ready, no publication until in-game audition.

## Status
Design and Valheim source inspection complete. No new source files or DLL modifications in this study. The existing installed 0.7.0 rainy-ground-LOS candidate is still the active experimental Enngard build, not the desired final architecture. WS unchanged.
