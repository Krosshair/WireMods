**SCRAPPED 2026-11-18:** built and installed, Kajetan saw no difference in looks or speed. Code and shaders reverted; the deferred version is in `backup/DEFERRED_PARTICLES_SCRAP_20261118` (RestoreSource.cmd puts it back). The weather one-pass shader stays. Note: Valheim's base steam, smoke and dust use Lux Lit Particles (one per-vertex lit draw, no per-light passes), which this never touched.

# Plan: Valheim's lit particles in the deferred pass, dithered

Goal: the small lit particles (rain drops, snow, splashes, smoke, sparks) draw once into the
G-buffer (alpha by dither) instead of forward (ForwardBase + one ForwardAdd draw per light per
system). Lighting, shadows, fog and the mod's tiled lights then come from the existing deferred
passes. No per-frame C# work.

## What the sources say (proof)

- Forward cost: Unity built-in forward draws every lit transparent once per affecting pixel light
  (ForwardAdd). Deferred draws it once into the G-buffer; lights cost per pixel, not per object.
  Unity only puts a material in the G-buffer if its queue is <= 2500, so the queue moves to
  AlphaTest (2450) and the shader needs a `LightMode=Deferred` pass.
- Fog: `assembly_postprocessing/UnityEngine.PostProcessing/FogComponent.cs` fogs deferred objects as a
  full-screen pass from depth at `CameraEvent.AfterImageEffectsOpaque` (only when the G-buffer exists),
  with `_SunDir` and `_SunFogColor` (lines 27-29, 38, 58), the same idea as LitParticles' own fog. So
  the new shader writes NO fog: whatever has depth in the G-buffer gets the game's fog.
- The mod's lighting (`WireLightShared.cginc` `WireShade`, called by `WireToonDeferredShading.shader`
  `CalculateLight` and the tiled path) reads G-buffer 0, 1 and 2.rgb only. G-buffer 2 alpha (2 bits)
  is unused by it and by Unity's lighting; solid shaders write 1 there. That is the particle flag.
- LitParticles' blend is fixed in the shader (alpha blend; its compiled tag is `RenderType=Transparent`, the exported project shows `Opaque` for every shader): no per-material
  blend, nothing additive to exclude.
- LitParticles lighting (replicated number for number in `LightRework/Assets/Shaders/WireLitParticles.shader`):
  `lerp(1, NdotL, _LightNormalFactor)`, no specular, SH ambient, `_EmissionColor`.
- Dithering: Enderton et al. 2010 "Stochastic Transparency" (coverage = alpha gives the right average;
  overlapping layers need uncorrelated noise); Wyman and McGuire 2017 "Hashed Alpha Testing";
  Jimenez 2014 "Next Generation Post Processing in Call of Duty: Advanced Warfare" (interleaved
  gradient noise, made to be resolved by temporal AA).

## Step 0 results (done, from the exported project's .mat files)

35 materials use `Custom/LitParticles` (shader guid 9c066ae7cba8b504eb127af91d0689d9). Full table:
`PARTICLE_MATERIALS.txt` next to this file.

- `_LightNormalFactor` is continuous (0, 0.158, 0.2, 0.23, 0.271, 0.44, 0.52, 0.66, 0.84, 0.9, 1),
  so 2 bits can't carry it. It goes into G-buffer 0 alpha instead (8 bits, see Step 2).
- Two kinds:
  - **Move (17, small sprites, soft fade <= 0.55 m):** rain_drop, rain_drops, rain_drop_indoor,
    rain_splash, water_drop, water_drop_noemissive, snow_flake, snow_cluster, winddust, windline,
    cinder_trail, tar_bubbles, tarsplash, ghost_smoke, wraith_smoke, grill_burnt_smoke, SmokeBlob.
    Losing a 0.3 m soft fade on a rain drop doesn't show.
  - **Stay forward (18, big planes / portals):** distant_fog, darklands_groundfog, hottub_planes,
    swamp_mist, heavymist, oceandrift, rain_soft, rain_fogclouds, distant_cloud_planes(_2),
    whirlwind2, snow_distant, tar_surface, menu_fogplane, menu_forestfog, exirt_portal,
    tointerior_portal (queue 2999), TheHoleRockHole_Mat. Their soft fade is 1-30 m (they'd show hard
    seams where they cut the ground) and they cover the screen (dither noise everywhere). Weather ones
    among them keep the existing one-pass forward shader.
- Cached maps: `ShadowCache.SeeThrough`/`FindCasters` skip every `ParticleSystemRenderer` by type, and
  the replacement render (`ShadowCache.cs:2120`) only matches `Opaque`/`TransparentCutout`. The new
  shader keeps the original `RenderType=Transparent` so the mod's effect-particle shadows
  (`SheetMaterial`, `ShadowCache.cs:1736`) behave as now. Queue 2450 is irrelevant there: particle
  renderers never reach `CastsShadow`.
- Swap points:
  - soft-ref assets: `SoftReferenceableAssets.AssetLoader.InvokeCallbacks(LoadResult)` runs once when
    an asset finishes loading, both sync (`Tick`) and async (`OnAssetLoadCompleted`); `Asset` holds
    the loaded object. Prefix there.
  - everything already loaded: one `Resources.FindObjectsOfTypeAll<Material>()` when the world starts.

## Step 1: new shader `Wire/DeferredParticles` (LightRework/Assets/Shaders)

Inputs and maths from `WireLitParticles.shader`, same property names (a material keeps its values
when its shader is swapped), one pass:

- Tags: `Queue=AlphaTest`, `RenderType=Transparent`, pass `LightMode=Deferred`, `ZWrite On`, `Cull Off`,
  no ShadowCaster pass. `#pragma multi_compile ___ UNITY_HDR_ON`.
- Vertex: as the replica (normal, colour, uv, world pos, camera fade, sky mask uv). Billboard path
  (SmokeBlob only). Camera Y fade and water line kept (cheap, vertex).
- Fragment:
  - albedo and alpha: `_Color * _MainTex * vertex colour`, `_AlphaChannel` (cinder_trail uses red),
    times camera fade, sky mask (8 of the 17 use it), below-water fade.
  - dither: `clip(alpha - noise)`; noise = interleaved gradient noise on pixel position + a
    per-particle offset + a per-frame offset only while temporal AA / DLSS / DLAA is on (a global the
    mod sets; static otherwise so it doesn't crawl).
  - outputs:
    - RT0 = albedo.rgb, a = `_LightNormalFactor`
    - RT1 = 0 (no specular, smoothness 0: Unity's deferred reflections add nothing)
    - RT2 = world normal * 0.5 + 0.5 (normal map with `_BumpScale`), a = 0 (particle)
    - RT3 = emission (rain/water drops have (1,1,1)) + SH ambient * albedo as the replica;
      `exp2(-x)` when `UNITY_HDR_ON` is off
  - no fog, no soft fade.
- Per-particle noise offset: the `Center` vertex stream (`ParticleSystemRenderer.SetActiveVertexStreams`),
  hashed. Without it overlapping smoke puffs with the same alpha cut the same pixels.
- Bundle: add to `BuildWireLightBundle.cs` ShaderPaths. Kajetan builds the bundle.

## Step 2: lighting shader reads the flag

- `WireToonDeferredShading.shader` `CalculateLight` (line 111-115) and the tiled callers already
  sample G-buffer 0 and 2; `WireShade` gets `g2.a`.
- `g2.a > 0.5` (all solid things): unchanged.
- `g2.a < 0.5` (particle): `nl = lerp(1, saturate(dot(n, l)), g0.a)`, no specular, no ToonRamp:
  LitParticles' own lighting. Shadow gate uses this `nl` (a sprite facing away from the light is
  still shadowed). `g0.a` is otherwise occlusion, which the mod's lighting doesn't read; Unity's
  deferred reflections multiply by it, but they're 0 here (RT1 = 0).
- Sun: same function, so sun light and Unity's screen-space sun shadows now reach particles.
- `WireAOApply`: skip AO on particle pixels (`g2.a < 0.5`).

## Step 3: C# (`ShadowCache.Particles.cs`)

- `Swap(Material m)`: shader is `Custom/LitParticles` and its name is in the move list ->
  remember shader and queue, `shader = Wire/DeferredParticles`, `renderQueue = 2450`.
- On world start: `FindObjectsOfTypeAll<Material>()` once, `Swap` each, and for
  `FindObjectsOfTypeAll<ParticleSystemRenderer>()` using a swapped material add the `Center` stream
  (prefabs included, so instances inherit it).
- Harmony prefix `AssetLoader.InvokeCallbacks` with `LoadResult.Succeeded`: the loaded `Asset`'s
  `ParticleSystemRenderer`s (GetComponentsInChildren, true): same swap + stream. Once per asset load.
- Setting "Particles in the lighting pass" (on/off). Off restores every remembered material and
  stream.
- The one-pass weather path stays for the forward weather materials (rain_soft, heavymist, ...).
  Rain drops leave it, so its per-frame light upload covers fewer systems.

## Step 4: side effects to check, fix only what shows

| Effect | Why | Fix if it shows |
|---|---|---|
| AO halo behind smoke | Amplify reads depth; particle pixels occlude what's behind | none cheap; partial since dithered |
| Water edges near rain | water shader reads scene depth, now with drops | take rain_splash out of the list |
| Smear with TAA/DLSS | particles write no motion vectors (as before) | none planned |
| Noise without temporal AA | dither is 1 bit per layer | static noise; or keep rain forward |
| Sorting | not needed any more | sortMode None on swapped renderers (CPU, proposal) |

## Order and backups

Each step its own backup (Restore.cmd, RestoreSource.cmd) and build: Step 1 + 2 (shaders, one
bundle) -> Step 3 (C#) -> Step 4 only for what he sees.

## Open decisions for Kajetan

1. Setting default on or off.
2. The move / stay split above.
