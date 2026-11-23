# WA 0.7.0 — Zero-extra-ray exterior environmental field
2026-11-23 | Experimental RC | In-game listening and profiler unverified

## Reason for replacement
User evaluated previous 16-patch/256-multi-hit-ray general environmental field as LAGGIER and still inaudible through a large open arch. A true emitter surface field is valuable in principle, but the prior implementation over-sampled a few arbitrary terrain circles and then normalized only the survivors incorrectly. It often placed candidates under the base roof and thus got no exposed samples, muting rain. Terrain rings, separate source-side roof rays and independent jobs were not the right fit for Valheim/WA.

## New implementation
- The field is the ANGULAR OUTDOOR INCIDENT SOUND from distributed exterior surfaces, integrated over existing WA room geometry. Not point emitters, not 16 simulated positions, not the visual rain particle system.
- Original 64-direction Fibonacci room survey already casts 3 RaycastCommands per direction = 192 room physics rays. Room.Out identifies genuine gaps vs one-ray seams. Room.AcousticBoundary selects actual boundary material from cached hit/neighbour when a seam is rejected.
- Each open exterior direction contributes unit gain / 22000 Hz. Each covered direction uses actual preclassified WearNTear/Steam Audio coefficients (Solids.Info.First/Cut) assembled through Occlusion.Block and evaluated by the existing Occlusion.Wall, SpectralPaths.Response/HighEnergy/Decode. Trees/props use already-authored soft weight. Direct ground under the listener is not assumed to be an exposed wet surface when the listener is inside.
- Sum low/high **energies** over solid angle with a fixed reference hemisphere; don't renormalize by however many open directions survived. Upward and near-horizontal/oblique-down rays above direction.y=-0.5 contribute, so large open arches remain exposed while intermediate floors block incoming rainfall. Outside unity gain is preserved by clamping to original outdoor reference.
- Applies one gain/cutoff to Valheim's original AudioMan.m_ambientLoopSource when actual current environment rain clip matches. It does not move the audio source, create extra voices, crossfade a roof layer, or fake more volume at windows.
- Entire removed separate job: zero EnvironmentalField RaycastCommands, NativeArrays, batch jobs, Heightmap scans, per-frame hooks or coroutines. No rain-specific 104-ray room job. One 64-direction loop during the already completed Room.Measure, only on listener position/world geometry/weather invalidation.
- 0 additional jobs vs the previous 256-ray field. Source length 166 lines; no async state/cleanup. Field explicitly general (EmitterDomain enum ExteriorSurface/AtmosphericVolume/Vegetation/Shoreline); only rainy weather audio loop currently routed through the new exterior-surface transfer. Wind/biome ambience and general SFX remain unchanged.

## Strong limitations, explicitly not solved
- The method estimates a diffuse exterior field at the room boundary. It does NOT find or individually sample every actually rain-exposed world surface. Such a literal distributed source requires more world geometry / zone data than Valheim exposes via ambient clips. This is a perceptual room-boundary approximation designed for 0 additional physics.
- The existing 64-direction survey can still miss very narrow, oblique openings. Source-linked simulation tests 8 open rays; does not prove the user's specific arch is recognized by Room.Out. To diagnose failures, turn on WA DebugLog and inspect 'environment field: open rays/blocked rays/direct energy/transmitted energy'.
- Multiple walls and basement floors beyond the FIRST cached surface aren't independently traversed; per-ray first material is the existing survey's best proxy. This isn't multi-bounce/convolution and doesn't invent material-specific raindrop recordings.
- A true global "all ambience" provider mapping (wave shoreline, wind in vegetation, bird point sources, atmospheric volumes) is not implemented. Those need their own physical source domains; the shared angular transfer model can be reused.
- No in-game audibility or runtime WA/WS profiler capture after this build; game was closed. One log today shows WA loaded, no patch diagnostics. No new profiler snapshot from 2026-11-23 existed in WiresProfiler folder at inspection time.

## Verification
- net472 Release build zero warnings/errors.
- dev/EnvironmentalFieldTests compiles production src/EnvironmentalField.cs with stubs for the underlying Unity/WA wall DSP, ~2076 direct-field tests (large arch gain 0.518, sealed stone 0.150, outdoors 1.0, two openings monotonic, timber vs stone, false seam, basement ground, spectral energy bounded).
- Full dev/VerifyReleaseCandidate.ps1 PASS, 52 production C# files, no new per-frame or coroutines, prior audio/wind/Fresnel/weapon/geometry regressions.
- Actual produced DLL SHA256 C49F0B39DD8C3CF0426595BA688B64BB58067268877959AE662E32D87E85447D.
- Package and Enngard installed, exact production DLL hash checked; staged ZIP verified against production DLL.

## Rollback
backup/ENVIRONMENT_FIELD_LIGHT_20261123/{src,dev,package,installed.dll}; Restore.ps1 (only when game closed).
No publish; WS unchanged.
