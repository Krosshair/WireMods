# Wires Acoustics 0.7.0: Complete Source Function Audit

Source inspected: C:\Users\kajet\WireMods\WiresAcoustics\src on 2026-11-22.
47 C# files; 421 named method-like members plus 4 anonymous lambdas (425 function bodies). Every row has a line number, summary from inspected implementation, and judgement.

## Prioritized code findings

| Priority | File and line | Source-level finding |
|---|---|---|
| P0 | Ambience.cs:87-131 vs ValheimSOURCE AudioMan.cs:290-332,633-645 | WA multiplies wind volume every render Update; Valheim assigns vanilla wind volume only in FixedUpdate. Multiple Updates between physics ticks multiply the same value repeatedly. During pause, FixedUpdate stops but Update can continue. This is a likely cause of pause-menu gain drift; needs in-game verification. |
| P0 | Budget.cs:103-107 and Filter.cs:554-583 | Estimated gain below 0.001 causes hard mute; filter zeroes audio under 0.0001. This can make an obstructed loud source completely silent. |
| P0 | Batch.cs:978-983; 209,217 | A whole audio tick shares 0.5 ms stopwatch budget before Batch can start new rays. Heavy or consistent overload could starve propagation. |
| P1 | Occlusion.cs:710-725 | Unchanged sources are still forced to recast after 2 seconds. 0.5-second Recheck traversal exists. This is periodic polling despite source comments. |
| P1 | Directions.cs:77-161 | Per-frame transform job schedules even when active sources stand still. Nonzero fixed CPU overhead; cost needs profiler. |
| P1 | SourceVolume.cs:75-102 | Finite 4/8/16/32 rays, count selected by angular thresholds; visibility may transition in audible steps. |
| P1 | Filter.cs:702-820 | HRTF has handmade interaural delay/head shadow/back-notch model rather than measured HRTF/Steam Audio convolution. |
| P1 | SpectralPaths.cs:20-25,95-112 and Occlusion.cs:387-426 | Real Steam Audio 3-band presets at 400Hz/2.5kHz/15kHz are mapped to DC+2kHz low-pass; third band discarded. |
| P1 | Room.cs:443-635 | First hit per room sample means opening fraction cannot account for a solid wall behind a known open panel. Room/reverb also has authored multipliers. |
| P1 | OpenPanelArea.cs:11-29 | Static table for seven prefab IDs: correct efficient design; aperture percentages are authored estimates. |
| P1 | Occlusion.cs:575-588; Ambience.cs; Ocean.cs:123-164 | Soft-material muffling, rain/ocean/wind subjective levels remain tuning rather than established physical law. |
| P2 | Ocean.cs:65-97; Room.cs:210-216 | Ocean rescans even when stationary every 4 sec; room scans Unity reverb zones periodically. |
| P2 | Batch.cs:587-950 | 363-line path solver with 50 branches, substantial change and stale-result state; high regression risk. |
| P2 | Occlusion.cs:381,544-574 | Old mass-law and collider-thickness helper methods no longer used by production wall path. |
| P2 | Plugin.cs:86-94 | Unity real voices increased from 32 to 128, trading virtualization for additional mixer work. |

## Positive engineering

Reused persistent native buffers, batched physics jobs, change-generation checks, source lifecycle cleanup, source provenance, shared edge caching and repeatable regression tests are meaningful positives. They do not establish in-game fidelity or CPU benefit on their own.

## All functions and explicit property bodies


### AcousticMath.cs (2 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| AcousticMath.OcclusionStrength(property) | 14 / 4 | Reads config: disabled gives 0, enabled clamps strength to 0-1.5. | Sound |
| AcousticMath.LogLerp | 19 / 2 | Interpolates low-pass frequencies in log space. | Sound |

### AcousticTrace.cs (5 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| AcousticTrace.Record | 31 / 19 | Records source target gain and cutoff changes in bounded history when debug is enabled. | Debug only |
| AcousticTrace.EmitTransitions | 51 / 23 | Formats recent gain/cutoff changes as source-tagged log lines. | Debug overhead |
| AcousticTrace.RecordGeometry | 86 / 11 | Records generation and position of world geometry changes for diagnostics. | Debug only |
| AcousticTrace.EmitGeometryChanges | 98 / 20 | Writes cached geometry-change events to log. | Debug overhead |
| AcousticTrace.Emit | 119 / 125 | Ranks tracked/playing sounds and logs detailed gain, DSP, coverage, path and meter snapshots. | Debug can be heavy |

### Ambience.cs (20 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Ambience.RememberPan | 62 / 6 | Saves the original stereo-pan setting to restore when switching off. | Sound |
| Ambience.RestorePlaced | 69 / 9 | Returns ambient loop sources and their panning to vanilla state. | Sound |
| Ambience.IsAmbient | 79 / 2 | Recognizes the game's own unplaced ambience AudioSource objects. | Sound |
| Ambience.Pan | 87 / 45 | Every rendered audio update: glides environment and multiplies vanilla wind volume for gust/lean; vanilla restores wind only on FixedUpdate, so gain can compound and drift during pause. | Hot; authored |
| Ambience.Lean | 134 / 10 | Creates/updates wind filter and steers its binaural direction/width. | Authored |
| Ambience.Update | 146 / 90 | Measures enclosure and exterior portals; assigns real ambient/wind loop copies to persistent slots. | Complex; authored |
| Ambience.Sounding | 238 / 9 | Checks whether an ambient copy is playing or still fading. | Sound |
| Ambience.Bed | 249 / 19 | Applies current indoor ambience gain/cutoff to the game's original loop. | Authored |
| Ambience.Glide | 281 / 40 | Smooths indoor state, shelter and weather; updates wind/rain filtering and sends. | Hot; authored |
| Ambience.WindFrom | 321 / 7 | Gets the current environment wind direction. | Sound |
| Ambience.Play | 331 / 32 | Starts or updates one copy of vanilla wind/ambient sound at a window slot. | Lifecycle risk |
| Ambience.Fade | 367 / 12 | Moves copy volume toward target and stops it when inaudible. | Sound |
| Ambience.Make | 380 / 22 | Constructs a persistent spatial AudioSource and AcousticFilter for portal ambience. | Lifecycle cost |
| Ambience.Apply | 403 / 16 | Assigns ambient semantics and low-pass to unplaced random or loop sounds. | Sound |
| Ambience.Forget | 421 / 6 | Removes one sound from the random ambience registry. | Sound |
| Ambience.Need | 429 / 6 | Marks an ambient filter for processing/budget tracking. | Sound |
| Ambience.Reset | 436 / 26 | Stops ambient copies and restores vanilla state. | Lifecycle |
| Ambience.Now | 464 / 13 | Formats detailed current portal/ambient state for diagnostics. | Debug |
| Ambience.Db | 478 / 1 | Converts amplitude to a dB string for log. | Debug |
| Ambience.Summary | 480 / 1 | Formats current ambient shelter gain, cutoff and enclosure. | Debug |

### Animated.cs (1 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Animated.Prefix | 15 / 19 | Intercepts effect creation and attaches animated sound emitters to moving effects/bones. | Valheim-specific |

### Area.cs (13 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Area.Invalidate | 26 / 17 | Drops cached source shape for pieces/descendants affected by geometry changes. | Sound |
| Area.InvalidateAll | 44 / 6 | Clears all cached source shapes. | Lifecycle |
| Area.Measure | 52 / 28 | Caches object/collider extents, centers sound emitters and adjusts fire source level. | Source geometry risk |
| Area.Voice | 85 / 18 | Moves a piece's sound toward its shape center, or flame light if a fireplace. | Valheim-specific |
| Area.TopOf | 106 / 7 | Returns cached top position of a collider-defined piece shape. | Sound |
| Area.CenterOf | 115 / 7 | Returns cached world-space source-center position. | Sound |
| Area.FlameOf | 124 / 13 | Returns light-based fire location, falling back to shape center. | Valheim-specific |
| Area.Cached | 138 / 13 | Retrieves or creates owner shape snapshot by instance ID. | Caching |
| Area.Of | 152 / 24 | Inspects child colliders to build local bounding shape of source owner. | Approximates geometry |
| Area.AddCollider | 181 / 28 | Updates combined bounds for supported collider shape. | Approximates geometry |
| Area.AddCorners | 210 / 15 | Transforms collider bounds corners into owner local space. | Geometry |
| Area.FireLevel | 231 / 6 | Assigns larger fireplaces higher authored volume by size. | Authored |
| Area.WidthAt | 239 / 2 | Computes apparent angular width from physical size/distance. | Geometry |

### AudioMeterHistory.cs (2 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| AudioMeterHistory.Publish | 26 / 13 | Publishes audio callback energy to a lock-free bounded ring with version markers. | Audio-thread safe intent |
| AudioMeterHistory.Read | 41 / 36 | Reads consistent recent ring packets and tallies missing data without blocking callback. | Debug |

### AudioProvenance.cs (7 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| AudioProvenance.Summary | 18 / 6 | Reports instrumented random ambient/lava sound calls. | Debug |
| AudioProvenance.ReplacePlay | 32 / 29 | Harmony-transpiles selected audio Play calls into wrappers that preserve source identity. | Patch fragility |
| AudioProvenance.MarkAndPlay | 62 / 12 | Tags spawned sound as world-random/lava before playing it. | Valheim-specific |
| AudioProvenance.PlayRandom | 75 / 1 | Delegates to MarkAndPlay with random-ambience provenance. | Valheim-specific |
| AudioProvenance.PlayLava | 76 / 1 | Delegates to MarkAndPlay with lava provenance. | Valheim-specific |
| Random.Transpiler | 81 / 3 | Patches random-ambient generator sound calls. | Patch fragility |
| Lava.Transpiler | 89 / 3 | Patches lava effect generator sound calls. | Patch fragility |

### Batch.cs (25 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Batch.Summary | 49 / 13 | Summarizes completed direct, volume, portal and corner rays and stale-job counters. | Debug |
| Batch.GeometryChanged | 66 / 42 | On nearby world edits, queues impacted active sources and invalidates shared edge cache. | Potential burst |
| Batch.Enqueue | 109 / 46 | Captures source pose/generation; skips inaudible or own sources; queues acoustic request. | Critical skip heuristic |
| Batch.Tidy | 181 / 13 | Deduplicates requests and sorts urgent/loud sources first. | Good prioritization |
| Batch.Step | 196 / 152 | Schedules bounded direct/volume/portal/corner raycast jobs and processes previous results. | Critical hot path |
| Batch.EnsureFlight | 349 / 8 | Resizes reusable request and geometry arrays geometrically. | Amortized allocation |
| Batch.Fly | 358 / 6 | Appends a request to the current in-flight batch. | Sound |
| Batch.StartProbes | 369 / 17 | Queues wall-edge probe requests for a missing cached edge. | Potential burst |
| Batch.Cast3 | 388 / 2 | Enqueues central plus two offset acoustic rays. | Finite sampling |
| Batch.Probe | 392 / 11 | Queues directed raycasts around an obstruction to locate usable edges. | Geometry |
| Batch.ReadProbe | 407 / 22 | Converts completed probe hits into cached edge candidate records. | Geometry |
| Batch.Open | 431 / 10 | Tests whether candidate ray leg is unobstructed. | Geometry |
| Batch.Cast | 442 / 8 | Constructs one RaycastCommand from two endpoints. | Geometry |
| Batch.DirectWalls | 459 / 25 | Runs three ray hit analyses and spectral-energy quadrature of direct path. | Finite sampling |
| Batch.DirectWalls | 485 / 5 | Runs three ray hit analyses and spectral-energy quadrature of direct path. | Finite sampling |
| Batch.VolumeRay | 493 / 8 | Evaluates independent source-volume sample with material transfer. | Finite sampling |
| Batch.Ray | 503 / 11 | Reads physics hits, sorts/combines geometry layers and terrain ridge. | Geometry |
| Batch.HeardFrom | 524 / 20 | Computes direction influence of late paths using hand-tuned precedence curves. | Authored psychoacoustics |
| Batch.Toward | 547 / 8 | Interpolates virtual perceived source toward a path waypoint. | Authored direction |
| Batch.ChangedDuringFlight | 559 / 25 | Rejects stale ray results if nearby geometry changed during job. | Correctness |
| Batch.Apply | 587 / 363 | Resolves direct, openings, edges, source volume, paths and DSP for each completed request. | 363-line critical complexity |
| Batch.Flush | 952 / 9 | Completes outstanding ray jobs and clears requests. | Lifecycle |
| Batch.Shutdown | 962 / 6 | Flushes and frees persistent ray buffers. | Lifecycle |
| Frame.Begin | 982 / 1 | Starts a 0.5-ms wall-clock budget per audio update. | Starvation risk |
| Frame.Over(property) | 983 / 1 | Compares current timestamp to the 0.5-ms deadline. | Starvation risk |

### Budget.cs (11 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Budget.Note | 20 / 13 | Registers/activates sound, turns on metering when debugging. | Sound |
| Budget.Toggle | 37 / 9 | Enables filter component with optional timing diagnostics. | Debug |
| Budget.Toggles | 47 / 7 | Reports how often filters were toggled. | Debug |
| Budget.Step | 56 / 54 | Every 0.25 s sweeps playing sources; mutes those estimated below -60 dB. | High; hard mute |
| Budget.Silence | 113 / 8 | Sets mute flag and makes muted sounds lowest Unity priority. | High; hard mute |
| Budget.Heard | 128 / 1 | Multiplies estimated unobstructed audibility by current wall gain. | High; proxy estimate |
| Budget.Open | 132 / 12 | Estimates gain from simple Unity attenuation shape, distance/source factors. | Risk: not actual mix |
| Budget.Clear | 145 / 6 | Clears tracked sources and unmutes them. | Lifecycle |
| Budget.Loudness | 154 / 29 | Aggregates per-source audio RMS meters and processing dB for log. | Debug cost |
| Budget.Virtual | 185 / 11 | Lists sources Unity virtualized because of voice limit. | Debug |
| Budget.Summary | 197 / 2 | Reports activation, muted sounds and mixer callback timing. | Debug |

### ContinuousEdges.cs (3 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| ContinuousEdges.Measured | 11 / 27 | Checks whether a cached edge is still geometrically valid around listener/source. | Stale geometry risk |
| ContinuousEdges.EvaluateVolume | 44 / 52 | Integrates Fresnel response across sampled source sphere for a validated edge. | Approximation |
| ContinuousEdges.Evaluate | 97 / 97 | Applies continuous single-edge diffraction using cached geometry without new ray jobs. | Geometry heuristic |

### Corners.cs (8 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Corners.From | 33 / 11 | Maps first/last obstruction hit into two wall-edge geometries. | Geometry |
| Corners.Dir | 46 / 8 | Constructs radial probe direction from obstacle face normal. | Geometry |
| Corners.Probes | 57 / 6 | Determines how many perimeter probes needed based on geometry/distance. | Heuristic |
| Corners.Probe | 64 / 1 | Returns specific perimeter probe ray direction. | Geometry |
| Corners.Covers | 86 / 8 | Tests if cached edge covers current listener/source position. | Geometry |
| Corners.Match | 95 / 5 | Finds matching wall edge record by position and normal. | Caching |
| Corners.Illuminated | 110 / 34 | Estimates diffraction even on clear side of cached wall edge. | Approximation |
| Corners.LengthGain | 145 / 6 | Applies geometric detour/distance loss for corner paths. | Acoustic approx |

### Cost.cs (6 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Cost.Start | 9 / 1 | Returns timestamp only with Debug log enabled. | Debug |
| Cost.Add | 11 / 7 | Adds elapsed time to per-system diagnostic statistic. | Debug |
| Cost.AddTicks | 19 / 5 | Adds already measured duration to statistic. | Debug |
| Cost.AddTick | 27 / 7 | Records whole audio update time and GC occurrence. | Debug |
| Cost.Ms | 35 / 1 | Converts ticks into formatted milliseconds. | Debug |
| Cost.Report | 37 / 8 | Formats/reset per-system cost counters. | Debug |

### Diagnostics.cs (1 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| RoomStats.Summary | 10 / 7 | Counts instance pools and reports resource usage when diagnostics enabled. | Debug |

### Diffraction.cs (9 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Diffraction.Diffraction | 16 / 15 | Constructs diffraction calculation's lookup table/constants at initialization. | Precompute |
| Diffraction.Loss | 32 / 8 | Converts detour distance and wavelength to predicted diffraction attenuation. | Acoustic approximation |
| Diffraction.RoundObstacle | 45 / 27 | Calculates bounded diffraction detour around an obstacle's box. | Approximation |
| Diffraction.Opening | 78 / 18 | Computes transfer through a discovered opening using width and path geometry. | Approximation |
| Diffraction.Lit | 98 / 8 | Calculates clear-side diffraction response for known obstacle edge. | Approximation |
| Diffraction.LitEdge | 112 / 7 | Converts edge geometry to additional colored response on illuminated side. | Approximation |
| Diffraction.DetourFor | 122 / 17 | Finds alternate path-length around object at a particular edge. | Geometry |
| Diffraction.RoundDetour | 142 / 9 | Applies diffraction loss to a known detour's path geometry. | Acoustic approximation |
| Diffraction.Round | 153 / 5 | Evaluates propagated sound bending around a single geometric edge. | Acoustic approximation |

### Directions.cs (8 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Job.Execute | 39 / 27 | Unity transform job reads source positions/distance and updates smoothed radial velocity. | Per-frame job |
| Directions.Want | 69 / 6 | Queues change to which filters need position tracking. | Efficient event registration |
| Directions.Step | 77 / 86 | Every audio frame completes previous job, updates panning/distance/Doppler and launches next transform job. | Per-frame overhead |
| Directions.Apply | 164 / 32 | Swap-add/removes tracked transforms and reuses arrays, synchronizing with job. | Efficient list management |
| Directions.Grow | 197 / 11 | Expands persistent NativeArrays to power-of-two capacity. | Amortized allocation |
| Directions.Idle | 210 / 5 | Flushes pending job and applies tracked-list changes while WA disabled. | Lifecycle |
| Directions.Flush | 217 / 4 | Completes any outstanding position-transform job. | Potential stall |
| Directions.Shutdown | 222 / 11 | Completes work, unregisters filters and disposes persistent arrays. | Lifecycle |

### Doppler.cs (2 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Doppler.ApplyOther | 25 / 14 | Writes pitch shift to non-ZSFX sources, preserving their original pitch. | Compatibility risk |
| Apply.Postfix | 48 / 17 | Updates ZSFX Doppler pitch from measured source/listener motion. | Compatibility risk |

### Equip.cs (13 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Equip.Take | 14 / 10 | Temporarily tags a character and equipment slot while creating related sounds. | Placement context |
| Equip.Release | 25 / 6 | Restores prior player/equipment tracking context. | Lifecycle |
| Equip.Vis | 32 / 1 | Fetches humanoid visual-equipment object for slot location. | Valheim-specific |
| Equipping.Prefix | 37 / 1 | Captures equip action before its effects spawn. | Hook |
| Equipping.Finalizer | 38 / 1 | Restores equip-scoped sound placement context even on exception. | Hook |
| Unequipping.Prefix | 44 / 5 | Captures unequip action and back-slot anchor. | Hook |
| Unequipping.Finalizer | 49 / 1 | Clears temporary unequip context. | Hook |
| Using.Prefix | 55 / 1 | Captures use-item sound context. | Hook |
| Using.Finalizer | 56 / 1 | Clears use context. | Hook |
| PickingUp.Prefix | 62 / 2 | Captures pickup item context. | Hook |
| PickingUp.Finalizer | 64 / 1 | Clears pickup context. | Hook |
| Dropping.Prefix | 70 / 1 | Captures dropped-item context. | Hook |
| Dropping.Finalizer | 71 / 1 | Clears drop context. | Hook |

### Filter.cs (47 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| AcousticFilter.GlobalGain(property) | 49 / 1 | Computes shared underwater level multiplier for this source. | Audio thread |
| AcousticFilter.GlobalCutoff(property) | 50 / 1 | Reads underwater-global low-pass cutoff. | Audio thread |
| AcousticFilter.RefreshOutputFormat | 58 / 10 | Checks Unity output device rate and invalidates spectral lookup when it changes. | Periodic |
| AcousticFilter.For | 71 / 3 | Gets or lazily adds an AcousticFilter component to source. | Lifecycle |
| AcousticFilter.SetGlobal | 75 / 6 | Writes global gain/cutoff state shared with active audio callbacks. | Audio thread coordination |
| AcousticFilter.DiagnosticsSummary | 93 / 12 | Formats worst audio callback timing and types. | Debug |
| AcousticFilter.TakeTicks | 105 / 6 | Atomically retrieves/reset audio callback timing totals. | Debug |
| AcousticFilter.Hrtf.get | 118 / 1 | Returns whether custom binaural processing is enabled. | State |
| AcousticFilter.Hrtf.set | 119 / 7 | Switches binaural processing; changes original panning and tracking. | State transition |
| AcousticFilter.Placed.get | 192 / 1 | Returns whether source has physical 3D placement. | State |
| AcousticFilter.Placed.set | 193 / 1 | Records placement and queues transform tracker update on change. | State |
| AcousticFilter.NeedsWork(property) | 197 / 3 | Checks gain/cutoff/distance/HRTF to determine whether DSP work needed. | CPU gating |
| AcousticFilter.AirAt | 201 / 8 | Interpolates precomputed ISO air absorption cutoff by distance/weather. | Physical approximation |
| AcousticFilter.AirTable | 223 / 18 | Computes frequency attenuation cutoff table for one weather case. | One-time CPU cost |
| AcousticFilter.AirAbsorption | 243 / 9 | Evaluates frequency-dependent ISO 9613-1 attenuation model. | Reference formula |
| AcousticFilter.PastRange | 259 / 5 | Adds custom extra attenuation outside vanilla sound's range. | Gameplay-tuned |
| AcousticFilter.TakePanning | 271 / 15 | Captures original spatial spread and replaces with custom binaural width. | HRTF approximation |
| AcousticFilter.Pan | 296 / 6 | Changes Unity stereo panning to avoid doubling custom spatial cues. | Integration |
| AcousticFilter.Tracked(property) | 324 / 1 | Determines whether source belongs in per-frame position job. | Per-frame scope |
| AcousticFilter.Awake | 328 / 16 | Registers AudioSource and Filter in live array and initializes sample format. | Lifecycle |
| AcousticFilter.OnDestroy | 345 / 16 | Swap-removes filter from live registry and queues tracker update. | Lifecycle |
| AcousticFilter.Active.get | 369 / 1 | Reads fast active-processing flag. | State |
| AcousticFilter.Active.set | 370 / 7 | Changes active-processing state without Unity component toggle. | Performance improvement |
| AcousticFilter.OnEnable | 379 / 5 | Resets filter history and reapplies binaural panning state. | Lifecycle |
| AcousticFilter.OnDisable | 385 / 4 | Restores proper panning when filter disabled. | Lifecycle |
| AcousticFilter.Set | 390 / 16 | Writes target gain/cutoff; optional instant snap or next-buffer fast crossfade. | DSP transition |
| AcousticFilter.TargetGain(property) | 407 / 1 | Returns target acoustic gain before shared distance/mix factors. | State |
| AcousticFilter.TargetCutoff(property) | 408 / 1 | Returns target acoustic low-pass cutoff. | State |
| AcousticFilter.DspGain(property) | 410 / 1 | Returns currently smoothed DSP gain. | State |
| AcousticFilter.DspCutoff(property) | 411 / 1 | Returns currently smoothed DSP cutoff. | State |
| AcousticFilter.EstimatedCompositeGain(property) | 412 / 1 | Estimates total DSP gain from target, water, distance and source level. | Debug/estimate |
| AcousticFilter.EstimatedCompositeCutoff(property) | 413 / 1 | Estimates smallest active cutoff among acoustic/water/air. | Debug/estimate |
| AcousticFilter.SetDirection | 415 / 1 | Stores local source direction vector for audio callback. | State |
| AcousticFilter.ReleaseHrtf | 418 / 10 | Disables binaural processing for every live filter. | Lifecycle |
| AcousticFilter.ReleaseAll | 430 / 19 | Restores every tracked source's original audio settings. | Lifecycle |
| AcousticFilter.Explain | 451 / 4 | Builds concise diagnostic string describing applied acoustic gain. | Debug |
| AcousticFilter.Line | 471 / 13 | Builds custom attenuation curve for line-like ocean source. | Gameplay-authored |
| AcousticFilter.Physical | 485 / 35 | Replaces Unity distance curve/doppler and restores previous settings when disabled. | Integration risk |
| AcousticFilter.Format | 521 / 5 | Summarizes aggregate tracked sound states for log. | Debug |
| AcousticFilter.EnsureMeterHistory | 531 / 4 | Creates bounded audio energy meter history on demand. | Debug allocation |
| AcousticFilter.ReadMeterHistory | 535 / 7 | Returns measured input/output audio energy from history. | Debug |
| AcousticFilter.Energy | 547 / 6 | Integrates sample-square energies in buffer. | Audio metering |
| AcousticFilter.OnAudioFilterRead | 554 / 73 | Main audio-thread callback: gates silence, processes/filter/meters PCM samples. | High; hard silence |
| AcousticFilter.Process | 628 / 55 | Applies per-channel two-stage low-pass and smooth volume when not binaural. | Audio-thread hot |
| AcousticFilter.SmoothOcclusion | 687 / 14 | Interpolates gain/cutoff per buffer over fast or 150ms transition. | DSP smoothing |
| AcousticFilter.Spatialize | 702 / 118 | Processes stereo through approximate ITD/head shading/back/notch delays and EQ. | 118-line authored HRTF |
| AcousticFilter.Glide | 822 / 1 | Returns buffer-time smoothing factor for 150ms glide. | DSP |

### Follow.cs (15 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Created.Postfix | 27 / 49 | On GameObject effect creation, attaches sounds to proper owner/emitting bone. | Valheim-specific |
| Felled.Postfix | 87 / 16 | Tracks tree-fall effects and places falling tree sound correctly. | Valheim-specific |
| Idle.Prefix | 108 / 1 | Temporarily records active owner of idle status sound. | Hook |
| Idle.Finalizer | 109 / 1 | Clears owner context after idle status. | Hook |
| Alerted.Prefix | 115 / 1 | Temporarily records creature issuing alerted sound. | Hook |
| Alerted.Finalizer | 116 / 1 | Clears alerted context. | Hook |
| Chest.Prefix | 126 / 5 | Marks owner/placement context when chests open or close. | Hook |
| Chest.Finalizer | 131 / 1 | Clears temporary chest owner context. | Hook |
| Follow.Moves | 137 / 11 | Checks/cache whether prefab's effect contains animated/transformed sound. | Caching |
| Follow.Step | 150 / 13 | Every audio update moves emitters with owners and cleans stale references. | Per-frame overhead |
| Follow.Track | 166 / 6 | Registers sound/owner transform pair for continuous following. | Lifecycle |
| Follow.Networked | 177 / 1 | Checks whether sound owner is network-synchronized. | Valheim-specific |
| Follow.Clear | 179 / 1 | Clears all tracked mobile emitters. | Lifecycle |
| Follow.Count(property) | 180 / 1 | Returns count of tracked sound followers. | Debug |
| Follow.Tracked | 182 / 5 | Checks whether a given sound transform is registered as a follower. | Lookup |

### FresnelEdge.cs (7 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| FresnelEdge.FresnelEdge | 28 / 22 | Precalculates normalized Fresnel integrals via Simpson quadrature. | One-time precompute |
| FresnelEdge.Sample | 51 / 7 | Interpolates odd-symmetric Fresnel lookup curve. | Math |
| FresnelEdge.Field | 61 / 7 | Constructs complex knife-edge field from C and S integrals. | Documented theory |
| FresnelEdge.PathDistances | 72 / 6 | Splits direct source/listener distance at wall crossing fraction. | Geometry |
| FresnelEdge.Parameter | 82 / 7 | Computes dimensionless Fresnel clearance parameter. | Documented theory |
| FresnelEdge.Intensity | 92 / 10 | Computes squared field including partially transmitting half-plane. | Approximation |
| FresnelEdge.Transfer | 103 / 23 | Fits two Fresnel frequency intensities into existing gain/two-pole cutoff. | Two-band limit |

### GeometryEvents.cs (11 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| GeometryEvents.Changed | 24 / 6 | Routes geometry event now or holds it until world area load stable. | Event |
| GeometryEvents.Apply | 33 / 6 | Invalidates area/room/propagation caches around changed piece. | Event response |
| GeometryEvents.AreaLoaded | 43 / 8 | Memoizes whether current chunk/area completed world loading. | Poll fallback |
| GeometryEvents.AreaReady | 61 / 14 | Checks ZNetScene/ZoneSystem/ZDO counts for area load completion. | Valheim-specific |
| GeometryEvents.Step | 77 / 8 | Periodically retries held world-load events and then flushes them. | Poll while pending |
| Built.Postfix | 89 / 5 | Invalidates local cache after placement. | Hook |
| Removed.Prefix | 99 / 5 | Invalidates local cache before removal. | Hook |
| TerrainEdited.Postfix | 112 / 4 | Signals geometry change after terrain edits. | Hook |
| MineRockAreaChanged.Postfix | 123 / 4 | Signals geometry change after mining rock terrain. | Hook |
| SceneryDestroyed.Prefix | 132 / 6 | Signals structural change before environment object destruction. | Hook |
| DoorChanged.Postfix | 143 / 5 | Signals door open/close state changes. | Hook |

### Hearing.cs (1 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Hearing.Update | 21 / 5 | Stores physical AudioListener position and parent camera for binaural orientation. | Listener model |

### Indoor.cs (2 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Indoor.Prefix | 16 / 6 | Suppresses/adjusts vanilla indoor reverb state when WA manages room reverb. | Compatibility |
| Paused.Prefix | 27 / 5 | Handles vanilla pause-state reverb/interior behavior. | Pause integration |

### Menu.cs (1 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Menu.Postfix | 12 / 18 | Finds existing AudioSources in newly opened menu and labels world/unplaced semantics. | Full source scan |

### Mist.cs (1 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Mist.Undo | 17 / 10 | Removes vanilla mist pitch-drop effect for audibility/spatial consistency. | Vanilla override |

### Occlusion.cs (37 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Occlusion.Resume | 30 / 7 | Uses completed first-sound raycast result and re-enters sound initialization with a known obstruction spectrum. | Start lifecycle |
| Occlusion.Resume | 38 / 10 | Uses completed first-sound raycast result and re-enters sound initialization with a known obstruction spectrum. | Start lifecycle |
| Occlusion.Postfix | 49 / 6 | Runs the acoustics after Valheim ZSFX reverb refresh and records debug timing. | Harmony hook |
| Occlusion.Run | 56 / 138 | Identifies ambience/cues, places sources, starts ray checks or updates filter gain, HRTF and reverb. | Main sound path |
| Occlusion.Send | 200 / 7 | Sends filtered sound to Unity room reverb, preserving original send level. | Integration |
| Occlusion.ReverbSend | 208 / 1 | Calculates direct/reverberant mix from distance and room critical distance. | Simplification |
| Occlusion.FacingPoint | 228 / 17 | Chooses listener-facing side of source's collider bounds for first sound ray. | Geometry approximation |
| Occlusion.Lines | 247 / 9 | Returns two small perpendicular ray offsets for aperture sampling. | Finite quadrature |
| Occlusion.Along | 269 / 51 | Sorts up to 16 ray hits, classifies walls/terrain/obstacles and combines spectral obstruction. | Hot; saturation |
| Occlusion.Counts | 326 / 13 | Excludes source-owned colliders and some enclosing colliders from acoustic obstruction. | Geometry heuristic |
| Occlusion.Inside | 340 / 8 | Tests whether source point lies inside collider via closest-point operation. | Physics check |
| Occlusion.Classify | 352 / 9 | Assigns object type/soft weight and material model once per collider cache. | Valheim-specific |
| Occlusion.Describe | 365 / 12 | Logs one-time sound identifier, source mode/shape and Unity rolloff. | Debug |
| Occlusion.MassLaw | 381 / 1 | Calculates physical wall mass-law factor; retained but not called by current material Loss. | Dead code candidate |
| Occlusion.TransmissionEnergy | 387 / 22 | Returns documented Steam Audio low/mid-band energy transfer for mapped wear types. | Two-band physical |
| Occlusion.Loss | 413 / 15 | Combines static open fraction with solid transmission energy; fits result to two-pole cutoff. | Authored porosity |
| Block.Clear(property) | 441 / 1 | Constructs identity transmission block with no walls/terrain/bending. | State |
| Occlusion.Layer | 446 / 17 | Merges nearby colliders into same wall; cascades spectral transfer for distinct walls. | Potential misgroup |
| Occlusion.Group | 470 / 16 | Unifies obstacles from same owner to avoid multiple attenuation. | Correctness |
| Occlusion.Ridge | 490 / 20 | Samples terrain heights along ray to approximate over-hill diffraction. | Terrain approximation |
| Occlusion.Wall | 514 / 27 | Combines soft obstacles, wall and diffraction energy into gain plus low-pass. | Two-band model |
| Occlusion.Mass | 544 / 18 | Estimates collider surface mass from material density and thickness; not used after model change. | Dead code candidate |
| Occlusion.Thickness | 563 / 11 | Extracts thinnest axis in world scale for mass-law estimate; now unused by active wall model. | Dead code candidate |
| Occlusion.Muffle | 575 / 11 | Interpolates an arbitrary soft-obstacle gain/cutoff table. | Not established standard |
| Occlusion.Voices | 589 / 22 | Applies the same spatial propagation pipeline to voice chat when enabled. | Compatibility |
| Occlusion.BaseSpreadOf | 614 / 4 | Reads Valheim ZSFX private base-spread field with fallback. | Version risk |
| Occlusion.Recheck | 625 / 29 | Sweeps all active sound filters every 0.5 s; updates loops and checks long SFX. | Polling |
| Occlusion.WorldChanged | 670 / 12 | Increments generation and invalidates affected acoustic routes on geometry events. | Event response |
| Occlusion.NearLine | 684 / 8 | Tests point-to-segment distance to decide change relevance. | Geometry |
| Occlusion.ChangedNear | 693 / 16 | Checks ring of last 64 world edits for changes near acoustic path. | Caching |
| Occlusion.Unchanged | 717 / 9 | Returns unchanged only if geometry and poses are stable AND last check is under 2 s. | Forced polling |
| Occlusion.HasNearbyWall | 727 / 10 | Looks for a recent validated wall edge close to the listener. | Geometry caching |
| Occlusion.Mine | 739 / 7 | Recognizes player's own footsteps/equipment to skip self-occlusion. | Valheim-specific |
| MyStep.Prefix | 753 / 5 | Marks player's footstep generator during effect creation. | Hook |
| MyStep.Finalizer | 758 / 1 | Unmarks footstep origin even after exception. | Hook |
| Occlusion.WatchLoop | 761 / 13 | Re-enqueues loop after movement/geometry change or elapsed fallback. | Polling |
| Occlusion.Summary | 775 / 13 | Formats and resets source check/wall/proxy hit statistics. | Debug |

### Ocean.cs (9 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Ocean.Postfix | 44 / 55 | On Valheim ocean update, updates shoreline scan, shade and moves vanilla ocean loop source. | Periodic scan |
| Ocean.RestoreUnlocated | 104 / 12 | Returns ocean loop to vanilla placement/rolloff when no shore is found. | Lifecycle |
| Ocean.Shade | 125 / 40 | Weights 16 observed shoreline bearings by room exposure, blends shelter gain and virtual direction. | Authored illusion |
| Ocean.Cast | 166 / 20 | Schedules 96 downward terrain rays in six radii around listener. | Fixed sampling |
| Ocean.Read | 192 / 31 | Finds nearest water/sea patch per direction and computes weighted shoreline direction/width. | Valheim-specific |
| Ocean.SeaZone | 231 / 12 | Caches Valheim ocean-depth classification per zone to reject ponds/rivers. | Good content use |
| Ocean.Shutdown | 244 / 10 | Stops and clears ocean geometry job, shoreline cache and position. | Lifecycle |
| Ocean.Dispose | 255 / 5 | Shuts down ocean cache and frees native raycast buffers. | Lifecycle |
| Ocean.Summary | 261 / 7 | Reports detected shoreline and skipped stationary scans. | Debug |

### OpenPanelArea.cs (1 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| OpenPanelArea.FromPrefab | 11 / 19 | Exact-name lookup of seven hardcoded open-area ratios; strips Clone suffix. | Correct cheap; unvalidated values |

### PlayerSounds.cs (5 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| PlayerSounds.RestoreAll | 17 / 17 | Restores spatial blend, distance and bypass settings of converted player sounds. | Lifecycle |
| PlayerSounds.Place | 37 / 27 | Converts own/other player's 2D sound into positioned 3D source with reversible snapshot. | Gameplay compatibility |
| PlayerSounds.Of | 65 / 14 | Locates player owning a sound through ancestors or nearby character lookup. | Source identity |
| PlayerSounds.IsCue | 80 / 6 | Recognizes skill/level-up and UI gameplay cues that should stay 2D. | Valheim-specific |
| PlayerSounds.MarkCue | 87 / 15 | Marks spawned cue audio components to bypass 3D placement. | Valheim-specific |

### Plugin.cs (4 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Plugin.Setting | 36 / 3 | Creates and binds BepInEx configuration entries under named category. | Initialization |
| Plugin.Awake | 40 / 36 | Registers config, patches Harmony systems and hooks diagnostics/voices. | Initialization |
| Plugin.MoreVoices | 86 / 9 | Raises Unity real voice count from 32 to 128 when possible. | Resource trade-off |
| Plugin.OnDestroy | 96 / 11 | Unpatches Harmony, restores vanilla settings and disposes native jobs. | Lifecycle |

### PortalLoop.cs (10 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Hold.Prefix | 39 / 2 | Records active portal teleport animation context. | Hook |
| Through.Postfix | 46 / 16 | Repositions portal arrival/teleport sound effects after transit. | Valheim-specific |
| PortalLoop.Take | 64 / 33 | Captures looping portal audio and its owner for position tracking. | Lifecycle |
| PortalLoop.Step | 99 / 43 | Maintains portal-loop sound spatial positions and crossfades active fades. | Per-frame |
| PortalLoop.Hand | 144 / 39 | Moves sound between active portal positions/parent and target orientation. | Spatial effects |
| PortalLoop.Let | 185 / 5 | Releases portal-owned audio when no longer needed. | Lifecycle |
| PortalLoop.BaseOf | 191 / 1 | Finds source portal base transform. | Valheim-specific |
| PortalLoop.Drop | 194 / 15 | Stops and restores tracked portal sound on despawn. | Lifecycle |
| PortalLoop.Clear | 211 / 6 | Releases all portal sound records. | Lifecycle |
| PortalLoop.Reset | 218 / 7 | Clears static portal tracking flags and caches. | Lifecycle |

### Portals.cs (5 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Portals.Find | 35 / 51 | Discovers candidate openings by room ray-gap patterns and estimates portal position/area. | Finite sampling |
| Portals.NeighboursOf | 87 / 5 | Obtains precomputed directional neighbor list for room rays. | Caching |
| Portals.BuildNeighbours | 93 / 40 | Builds nearest-neighbor topology for 64 room sample directions. | One-time precompute |
| Portals.LengthGain | 134 / 6 | Attenuates routed path by relative source-to-listener detour length. | Acoustic approximation |
| Portals.Summary | 141 / 7 | Reports candidate openings, directions and sizes for debug. | Debug |

### Prefabs.cs (1 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Prefabs.Postfix | 16 / 26 | On scene prefab init, walks sound prefabs, labels placement and adjusts emitter counts. | One-time scan |

### RayJobs.cs (7 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| RayJobs.Query | 13 / 3 | Builds query parameters for chosen physics layer mask. | Physics wrapper |
| RayJobs.Ensure | 17 / 9 | Allocates/grows persistent NativeArrays of commands and hits as needed. | Amortized allocation |
| RayJobs.EnsureFixed | 29 / 7 | Allocates exact-size persistent arrays when layout changes. | Buffer management |
| RayJobs.Submit | 37 / 8 | Schedules batched Unity RaycastCommand worker job. | Asynchronous |
| RayJobs.Cast3 | 46 / 8 | Builds three parallel raycasts around direct line. | Finite sampling |
| RayJobs.ReadHits | 58 / 12 | Compacts non-null ray hits into fixed scratch buffer. | Capacity bound |
| RayJobs.Dispose | 71 / 5 | Disposes native raycast command/hit arrays. | Lifecycle |

### Room.cs (35 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Room.Invalidate | 47 / 1 | Marks room sample geometry invalid, triggering remeasurement. | Event response |
| Room.SpeedAmbientAtBoundary | 76 / 13 | Temporarily shortens Valheim's ambient fade at indoor/outdoor transition. | Gameplay tuning |
| Room.RestoreAmbientFade | 90 / 8 | Restores previously saved vanilla ambient crossfade time. | Lifecycle |
| Room.Mask.get | 101 / 5 | Lazily creates physics layer mask for world/terrain/pieces. | Caching |
| Room.BuildDirs | 108 / 11 | Generates 64 quasi-uniform sphere directions with golden-angle spacing. | Finite sampling |
| Params.Lerp | 123 / 7 | Interpolates Unity reverb parameter snapshot. | DSP environment |
| Params.ApplyTo | 131 / 11 | Writes generated reverb parameters to one Unity AudioReverbZone. | Integration |
| Params.Differs | 143 / 5 | Tests whether latest room reverb params changed materially. | CPU gating |
| Params.ToString | 149 / 3 | Formats current room reverb parameters for debug. | Debug |
| Tick.Postfix | 160 / 69 | Main WA per-frame host hook on AudioMan.Update; runs all room/audio subsystems and jobs. | Critical scheduling |
| Tick.Run | 230 / 57 | At scheduled interval completes room job, computes statistics and queues next sample rays. | Periodic sampling |
| Room.EnsureArrays | 289 / 2 | Creates/resizes persistent raycast arrays for room sampling. | Buffer management |
| Room.Setup | 292 / 24 | Creates/reparents global Unity reverb zone attached to listener. | Audio integration |
| Room.RemoveZone | 319 / 14 | Disables original game reverb zone while saving its state for restore. | Compatibility risk |
| Room.RemoveVanilla | 334 / 6 | Scans live Unity AudioReverbZones and disables incompatible vanilla ones. | Periodic full scan |
| Room.RestoreZones | 341 / 11 | Returns original reverb-zone enabled states and removes WA zone. | Lifecycle |
| Room.Restore | 353 / 17 | Resets portal/room/ambient state and pending cast when WA disabled. | Lifecycle |
| Room.Shutdown | 371 / 8 | Stops room jobs and frees native buffers. | Lifecycle |
| Room.Exposure | 389 / 16 | Weights measured directional room openings toward a given wind/ocean direction. | Finite sampling |
| Room.Out | 407 / 1 | Reports whether a room ray escapes through accepted opening. | Geometry |
| Room.PortalEvidence | 410 / 1 | Separates a measured built-geometry opening from open sky. | Geometry |
| Room.WriteLog | 418 / 11 | Emits aggregated per-system room, sound, budget and performance reports. | Debug overhead |
| Room.Pick | 434 / 8 | Chooses first room ray hit or offset-neighbor hit to suppress tiny gaps. | Seam heuristic |
| Room.Measure | 443 / 191 | Integrates 64 room rays into size, absorption, enclosure, RT60, wetness and reverb. | 191-line complex hybrid |
| Room.Surface | 644 / 23 | Classifies one ray's first hit as ground/water/wall, assigns absorption. | First-hit limit |
| Room.Furniture | 671 / 10 | Maps comfort-type props to 500Hz absorption coefficients. | Approximation |
| Room.Word | 684 / 11 | Recognizes complete lexical tokens in prefab names. | Valheim-specific |
| Room.GroundClassAt | 708 / 28 | Determines ground/biome material under sample ray. | Valheim-specific |
| Room.GroundAt | 737 / 5 | Maps current ground texture to a rough absorption class. | Approximation |
| Room.Absorption | 745 / 27 | Assigns surface sound absorption based on prefab class/material and ground. | Material lookup |
| Solids.Of | 805 / 6 | Looks up or lazily classifies physics collider by instance ID. | Caching |
| Solids.Of | 812 / 6 | Looks up or lazily classifies physics collider by instance ID. | Caching |
| Solids.BoxOf | 819 / 6 | Returns bounds, refreshing cached static bounds when door generation changes. | Caching |
| Solids.IsCoarseOpenPanel | 831 / 30 | Recognizes seven Valheim open-panel collision proxies by exact name/shape. | Exact-name authoring |
| Solids.Make | 862 / 41 | Builds collider type/material/open-area/absorption cache record. | Critical classification |

### SharedEdges.cs (4 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| SharedEdges.Clear | 28 / 7 | Clears shared cached diffraction edge records. | Lifecycle |
| SharedEdges.TryCopy | 38 / 37 | Reuses validated nearby edge geometry rather than recasting for each source. | Optimization; staleness |
| SharedEdges.Publish | 76 / 22 | Publishes newly measured edge record for nearby sources. | Caching |
| SharedEdges.Summary | 99 / 7 | Reports shared-edge hits and cache misses. | Debug |

### SourceVolume.cs (4 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| SourceVolume.Halton | 21 / 11 | Generates low-discrepancy deterministic sample coordinate in chosen base. | Sampling |
| SourceVolume.Project | 33 / 41 | Builds spherical source bounds from owner colliders or configured fallback radius. | Geometry approximation |
| SourceVolume.Count | 75 / 15 | Chooses 4,8,16,32 source rays based on angular radius and cap. | Quantized quality |
| SourceVolume.Point | 91 / 12 | Maps Halton samples to uniform sphere volume points in world orientation. | Finite sampling |

### Spawned.cs (5 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Spawned.Take | 14 / 15 | Scans spawned location/game objects for AudioSources and reverb zones and marks them. | One-off scan |
| AtLocation.Postfix | 33 / 1 | Processes newly spawned location sounds. | Hook |
| AtDungeonRoom.TargetMethod | 43 / 5 | Finds dungeon-room generation function via reflection. | Version risk |
| AtDungeonRoom.Postfix | 48 / 4 | Processes generated dungeon-room sound sources. | Hook |
| Spawned.Summary | 54 / 6 | Reports previously registered location sounds and suppressed native zones. | Debug |

### SpectralPaths.cs (9 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| SpectralPaths.BandK | 27 / 5 | Precomputes digital angular-frequency 2kHz response constant. | DSP math |
| SpectralPaths.SetSampleRate | 33 / 6 | Rebuilds 2kHz reference constant if Unity output rate changes. | Format handling |
| SpectralPaths.Response | 40 / 12 | Calculates exact 2kHz power response of WA's two cascaded one-pole DSP stages. | Validated DSP model |
| SpectralPaths.HighEnergy | 53 / 4 | Returns gain-squared times 2kHz power response. | DSP math |
| SpectralPaths.Add | 58 / 6 | Accumulates incoherent pathway energies at DC/2kHz. | Energy sum |
| SpectralPaths.CutoffFor | 69 / 25 | Analytically inverts desired 2kHz transfer into implementable cutoff. | DSP approximation |
| SpectralPaths.Decode | 95 / 18 | Fits low+mid energies to a single gain and cutoff, clamping energy above unity. | Two-band limit |
| SpectralPaths.Cascade | 114 / 8 | Multiplies serial path energy spectra, then refits DSP. | Acoustic approximation |
| SpectralPaths.Combine | 123 / 9 | Sums two independent path energies, then refits gain/cutoff. | Acoustic approximation |

### Spots.cs (16 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Spots.Mask(property) | 36 / 2 | Lazily creates physics layer mask for sound attachment guesses. | Caching |
| Spots.Clear | 39 / 1 | Clears temporary source placement/equipment context. | Lifecycle |
| Spots.Place | 42 / 66 | Resolves effect sound transform to weapon, player, door, workstation or piece sound position. | 66-line heuristic |
| Spots.Note | 110 / 5 | Records current sound-triggering effect and its source object/position. | Placement context |
| Spots.Note | 116 / 6 | Records current sound-triggering effect and its source object/position. | Placement context |
| Spots.Attacking | 124 / 9 | Tests whether a character is currently executing an attack animation. | Valheim-specific |
| Spots.WeaponHand | 136 / 12 | Maps equipped weapon/hand to preferred sound attachment transform. | Valheim-specific |
| Spots.ItemSound | 150 / 9 | Locates world origin of item-pickup/drop/equipment audio. | Valheim-specific |
| Spots.CharacterAt | 160 / 6 | Resolves nearby character owning a sound effect at a point. | Search |
| Spots.KindOf | 173 / 9 | Categorizes source effect/piece based on known prefab names and ancestry. | Heuristic |
| Spots.Move | 184 / 5 | Moves a ZSFX emitter to corrected source location. | Integration |
| Spots.MoveOne | 190 / 11 | Moves or parents an individual source while preserving relative transform. | Integration |
| Spots.Hinge | 208 / 14 | Finds moving gate/door hinge transform for opening sounds. | Valheim-specific |
| Spots.StationPart | 226 / 25 | Finds moving part on kiln/smelter/crafting station to anchor sound. | Valheim-specific |
| Spots.PieceAt | 253 / 13 | Searches nearby placed building piece to attach effect sound. | Search/perf risk |
| Spots.BackSlot | 268 / 17 | Finds player back-slot/weapon holder to attach equipped item sound. | Valheim-specific |

### Stackify.cs (12 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Stackify.Active(property) | 19 / 1 | Returns whether Stackify audio compatibility patch is currently active. | Compatibility |
| Stackify.Restore | 22 / 4 | Restores saved output mixer group of chest sound. | Compatibility |
| Opening.Prepare | 30 / 1 | Only enables patch when optional Stackify mod is present. | Optional hook |
| Opening.TargetMethod | 31 / 1 | Finds Stackify opening method by reflection. | Version risk |
| Opening.Prefix | 32 / 1 | Records active chest opening context. | Hook |
| Opening.Finalizer | 33 / 1 | Clears temporary opening flag/output group. | Hook |
| Near.Prepare | 39 / 1 | Guards patch if Stackify integration installed. | Optional hook |
| Near.TargetMethod | 40 / 1 | Reflects Stackify's nearest container method. | Version risk |
| Near.Postfix | 41 / 4 | Redirects sound's container source to actual selected container. | Compatibility |
| PlayNow.Prepare | 50 / 1 | Guards optional Stackify audio play patch. | Optional hook |
| PlayNow.TargetMethod | 51 / 1 | Finds Stackify's audio playback routine. | Version risk |
| PlayNow.Prefix | 52 / 6 | Captures and preserves mixer group before Stackify plays chest audio. | Compatibility |

### Starts.cs (11 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Starts.Summary | 40 / 8 | Formats how many new sounds had start delayed for acoustic rays. | Debug |
| Starts.Hold | 53 / 5 | Puts a new source in pending-start queue before playing, to resolve occlusion first. | Sound latency |
| Starts.HoldAir | 60 / 7 | Puts random outdoor one-shot in pending queue for open-air placement. | Source placement |
| PlayEnd.Postfix | 73 / 8 | Cleans up first-play state after ZSFX.Play actually starts. | Lifecycle |
| Starts.Travel | 84 / 9 | Calculates estimated first-arrival propagation delay from source to listener. | Gameplay option |
| Starts.Step | 96 / 103 | Finishes pending first-sound rays then initializes playback with geometry/underwater state. | 103-line critical |
| Starts.Schedule | 201 / 60 | Schedules finite direct, source volume and open-air source cast jobs. | 60-line scheduling |
| Starts.Cast3 | 262 / 2 | Enqueues three offset first-play raycasts. | Finite sampling |
| Starts.PlaceInOpenAir | 267 / 27 | Relocates random exterior ambience source above nearby ground and roof. | Valheim-specific |
| Starts.Flush | 296 / 9 | Completes and clears pending first-play raycast job. | Lifecycle |
| Starts.Shutdown | 306 / 5 | Flushes and disposes first-play persistent buffers. | Lifecycle |

### ThreeRayTransmission.cs (3 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| ThreeRayTransmission.Blend | 11 / 10 | Energy-interpolates two sampled direct paths by face share. | Finite quadrature |
| ThreeRayTransmission.IndirectAmplitude | 25 / 2 | Returns square root of hidden fraction for indirect route amplitude. | Acoustic assumption |
| ThreeRayTransmission.Combine | 28 / 15 | Weights center and two side ray energies, returns gain/cutoff/blocked fraction. | Finite quadrature |

### Underwater.cs (11 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Underwater.Apply | 24 / 4 | Applies underwater gain and cutoff to one acoustic sound. | Medium model |
| Underwater.Apply | 29 / 5 | Applies underwater gain and cutoff to one acoustic sound. | Medium model |
| Underwater.High | 37 / 1 | Applies underwater low-pass spectral adjustment to gain/cutoff. | Medium model |
| Underwater.Below | 39 / 1 | Tests world position under current water surface. | World geometry |
| Underwater.SubmergedAmount | 41 / 7 | Computes smooth fraction submerged near water surface. | Transition |
| Underwater.Muffles | 53 / 1 | Checks whether source lies under water and should use underwater path. | Medium model |
| Underwater.SourceAmount | 58 / 19 | Computes source/submarine path water proportion by geometry. | Approximation |
| Underwater.CharacterAt | 80 / 15 | Tests whether sound belongs to a character currently underwater. | Valheim-specific |
| Underwater.Update | 97 / 6 | Updates global listener water gain/filter based on position. | Periodic |
| Underwater.Reset | 104 / 6 | Resets global underwater attenuation and filter. | Lifecycle |
| Underwater.Summary | 111 / 1 | Reports listener submerged fraction. | Debug |

### VanillaShortAmbient.cs (1 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| VanillaShortAmbient.Prefix | 17 / 33 | Adjusts short vanilla ambient sound spawn location to better match acoustic scene. | Compatibility |

### VoiceChat.cs (9 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| Build.Prepare | 20 / 1 | Checks whether proximity voice stream type exists before patching. | Optional hook |
| Build.TargetMethod | 22 / 7 | Reflects private voice stream source method and field. | Version risk |
| Build.Postfix | 30 / 7 | Initializes newly built voice chat AudioSource for WA spatialization. | Compatibility |
| FromHead.Prepare | 44 / 1 | Checks whether spatial voice renderer exists. | Optional hook |
| FromHead.TargetMethod | 46 / 5 | Reflects spatial-voice source location routine. | Version risk |
| FromHead.Postfix | 52 / 10 | Moves voice emitter to character head transform. | Valheim-specific |
| NoMuffle.Prepare | 68 / 1 | Checks if third-party voice muffling routine exists. | Optional hook |
| NoMuffle.TargetMethod | 70 / 5 | Finds third-party SetMuffle method. | Version risk |
| NoMuffle.Prefix | 76 / 6 | Disables overlapping third-party voice low-pass/volume so WA owns acoustics. | Compatibility tradeoff |

### VolumeFresnel.cs (1 methods)

| Function | Source line / LOC | How it works | Assessment |
|---|---:|---|---|
| VolumeFresnel.Transfer | 10 / 26 | Evaluates Fresnel screen crossing and two-band response for one volume sample. | Single-edge approximation |

## Anonymous function bodies (4 lambda expressions)

| Function | Source line | Mechanism | Assessment |
|---|---:|---|---|
| Batch.Loudest comparator | Batch.cs:170 | Sorts pending requests by urgent change, probe status, expected loudness, then distance. | Hot sorting path. |
| Budget.Levels.Sort comparator | Budget.cs:178 | Sorts diagnostic gain changes in ascending dB order to report worst attenuation first. | Debug only. |
| Portals.List.Sort comparator | Portals.cs:83 | Ranks detected portal openings by their estimated aperture share. | Finite topology. |
| Portals.neighbour comparator | Portals.cs:110 | Sorts directional neighbors by angular proximity with index tie-break. | One-time topology setup. |

## Limits

This audit is static code review, not execution inside Valheim. No performance trace, listening verification or exhaustive mod-compatibility test was completed in this audit.
Reference for material 3-band energy transmission: https://valvesoftware.github.io/steam-audio/doc/capi/scene.html
Source inventory: WA_METHOD_INVENTORY_20261122.tsv
Human function summaries: WA_REVIEW_DESCRIPTIONS_20261122.txt
Roslyn extractor: dev/ReviewInventory/Program.cs
No production source or installed DLL was changed as part of this source review.
