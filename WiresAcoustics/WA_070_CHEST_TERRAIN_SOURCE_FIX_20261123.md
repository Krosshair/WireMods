Wire's Acoustics 0.7.0 RC - physical chest sources and terrain raise placement
Local candidate, not released.

- Normal chest open/close: EffectList.Create receives lid location before first play; ZSFX.Play already knows the owner and surface emitting semantics.
- Stackify chest sounds: source placed at real lid, attributed to its Container before playback.
- Chest source volume: use already available 4/8/16/32 source rays on projected, listener-facing real panel surfaces rather than treating chest opening as a point inside a stacked chest. Real oriented shape; visible side plus top candidates.
- Terrain raising: TerrainOp.OnPlaced event keeps context; EffectList.Create receives an elevated origin computed from actual heightmap where available and authored positive terrain raise delta, with small clearance.
- No new custom frame hooks, coroutines, synthesized audio, or environment jobs. Increased volume samples only when chest sounds actually play.
- Builds cleanly; full VerifyEveryImplementation.ps1 pass, including new source-linked terrain and chest-shell tests.
- Not yet audibly verified in game; test stacked chests, Stackify quick-stack, raising terrain, ordinary non-raise terrain and controls.
Installed DLL SHA256: 39D15E146CE16AC63951A304C551212368DEA373708B2D7DD5036E14DD30E90E
Rollback DLL: C:\Users\kajet\WireMods\WiresAcoustics\backup\CHEST_TERRAIN_SOURCE_FIX_20261123\Installed-WiresAcoustics.dll
Candidate zip: C:\Users\kajet\WireMods\WiresAcoustics\staging\WiresAcoustics-0.7.0-RC-chest-terrain-sources-20261123.zip