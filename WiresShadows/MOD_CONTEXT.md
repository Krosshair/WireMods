# Wire's Shadows: context

0.6.0 ready to publish (2026-11-26). Cached point-light cube shadow maps for placed lights, moving-caster maps, custom deferred lighting shader, coloured AO, contact shadows. Status log: `STATUS.txt`.

## Architecture
- Shaders: source of truth `C:\Users\kajet\LightRework\Assets\Shaders` (Unity 6000.0.75f1), copies in `shaders\`. He builds the bundle (menu "Wire > Build Light Occlusion bundle" -> `build\wirelightocclusion`), copy to `src\wiresshadows.bundle` (embedded), then build the DLL.
- One path only (EnsureResources): native depth + sized pools (4 sizes by on-screen reach) + one-pass faces (front R / back G, combine pass 4 to depth). Otherwise vanilla point shadows.
- Cached faces have no camera (0.6.0): FindCasters' collider search (plus colliderless scenery listed at ZNetView.Awake, Scenery.cs) fills t.Direct/Explicit/Sheets/proxies; DrawFaceCore records one command buffer per face with the moving maps' matrices. WireShadowFrontBack: 0 sheets, 1 models (front/back by triangle normal turned outward by mesh normals, not winding), 2 collider backs.
- Maps only grow (no timed downsizing); ShrinkOversized frees room when memory runs short. Keep.cs reuses maps by resting position + range + CasterHash. AreaWait.cs waits for ZNetScene.IsAreaReady.
- Moving shadows: batched geometry-shader path, per-face fallback without GS. Character faces are drawn whole (no per-renderer ScreenRect).
- AO: swaps Amplify Occlusion's apply shader for WireAOApply (Amplify MIT copy + GTAO multi-bounce). Contact shadows: Bend Studio port (Apache 2.0), ray start lifted off its surface (StartDepth, normal offset by the surface thickness).
- Cached map lookup: normal offset one texel; casters pulled in along normals (max(1 texel, 1 cm)).

## Why / tried and removed
- Bounce light, sun layer culling, deferred particles, per-light GPU culling (LightCull, +5 ms indoors): removed. Point-light contact shadows (2026-11-26): "not worth it", in `_removed\POINT_CONTACT_20261126`.
- Known incompatibility: Badgers Shading Overhaul (same lighting shader).
- Grass never was in cached maps (InstanceRenderer layer, 17/18 prefabs cast nothing).
- Always check the installed DLL hash before trusting a benchmark. Captures right after spawning or a benchmark measure the first fill (RenderDirty), not the steady cost.
- He judges RenderDirty as the mod's main cost; the slow 1-face-a-frame fill is fine to him (no visible pop-in).

## Open
- Possible gaps of camera-less drawing: meshes with flipped normals (shadow start off), DvergerArbalest (colliders only on hidden parts).
- WireShadowDistance's WIREGS / WIRESHEETGS passes are unused since the cleanup; drop them at the next bundle rebuild.
- Benchmark vanilla runs throw every map away (ReleaseAll + DropAllKept); keeping them would drop the benchmark's refill wait. Offered, not decided.
- Blocky self-shadows on faceted stone where light skims it (terminator). A slope-scaled receiver offset did not help.

