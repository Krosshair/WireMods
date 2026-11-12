# Point-light indirect illumination / GI feasibility in WPR (research only)
Date: 2026-11-08. NO code, shaders, DLLs, BepInEx configuration, light sources or runtime behavior changed.

## Diagnosis and key distinction
Valheim Built-in deferred renders realtime direct illumination and dynamic shadow cubemaps. When a wall blocks the direct path from a fireplace, the fireplace's contribution to a receiver is suppressed, but a real room would still get illumination reflected from nearby surfaces. GI is different from animating or brightening shadow shapes, which the user disliked. Goal: physically anchored, low-frequency diffuse *secondary* illumination, no artificial shadow wobble, no light leaks, and tiny GPU/CPU budget.

Sun and directional shadows: realtime sun shadow casters (including walls/roofs marked static) are usually rasterized into cascaded shadow maps each camera frame. Static means stable mesh/transform/batching data, NOT reusable shadow-map pixels as the directional light and camera-relative cascade matrices change. Can avoid repeated CPU object/mesh/material collection through persistent indirect GPU draw buffers; VPO indirect renderer already implements similar strategy (not necessarily active in user's Enngard profile). Does not eliminate GPU shadow rasterization. Sun optimization is secondary to GI.

## Candidate techniques
A. RSM / sparse surfels (best architectural fit for WPR):
- WPR already has cached 6-face point-light depth maps, updated on light/geometry events. Use them as primary-light visibility and sampled first-hit geometry.
- Select 8–32 stable first-bounce surfaces around the *highest-priority nearby firelights*, reconstruct world positions from direction and shadow depth, and estimate a surface normal using neighboring depths; improve robustness with optional cache-time normal/albedo data if needed. Existing WPR shadow cache stores distances (and sometimes front/back midpoint), NOT full albedo/normal/flux; cannot get a correct reflective color for free.
- Each surface patch is a VPL/surfel; treat its diffuse reflection (surface albedo x incoming fire radiance x cosine) as a weak SECONDARY emitter, with physically reasonable falloff and area-clamped intensity. Preserve the existing exact primary-source visibility and shadow mask; do not undo the direct shadow.
- The difficult part is bounce-point -> receiving-pixel visibility: primary cubemap only tests primary->caster, NOT caster->receiver. A naive unshadowed bounce emitter will leak through walls. First proof can restrict surfels to a small radius / common room and use bounded GPU depth/ray checks or sparse scene-cell connectivity/occlusion masks. Avoid full additional shadow maps per surfel.
- Surfels for static geometry may be cached and invalidated by the same registration/piece-change events WPR already handles, so no CPU per-frame scan. Fire intensity can update surfel radiance with the existing light intensity with no geometry rebuild. GPU still evaluates indirect contribution when lit pixels shade; use light-volume culling / coarse-tile grouping and cap surfel count.
- Start with ONE isolated fireplace and one test room, one wall / wall corner. Test correct attenuation, surface tint, blocked room, door open/closed, moving player, clipped geometry, and FPS. Then support 2–4 lights, and only later crowded bases.
- RSM classic reference: Carsten Dachsbacher and Marc Stamminger, Reflective Shadow Maps, SI3D 2005 DOI: 10.1145/1053427.1053460. One-bounce shaders store depth, world-space position, normals, and reflected flux. Valheim/WPR would need to reconstruct/capture the missing data.

B. Screen-space GI (fast prototype, fragile production):
- Built-in deferred G-buffer has RT0 diffuse albedo, RT2 normal, scene depth, and later RT3 lighting. A half-resolution (or quarter) screen-space ray march can use current visible surfaces and fire lighting to approximate one bounce.
- Strong constraints: flames/lighted surfaces behind the camera or off-screen cannot contribute; images pop as camera turns; thin objects cause ghosting and depth artifacts. Reprojection/history smoothing needed (not a shadow wobble; can create other artifacts). Can be a low-cost appearance experiment, not reliable whole-room GI.

C. Sparse voxel/probe GI (highest consistency, higher engineering cost):
- Localized occupancy/irradiance volume only around active WPR fires, updated on construction/destruction and light changes; sample probes and approximate visibility to reduce wall leaks. Larger memory, GPU compute and invalidation complexity. Could become real-time dynamic GI without dependence on viewport.
- Unity 2026 Surface Cache GI preview exists in later rendering pipelines, but cannot be simply plugged into Valheim's Unity 6000.0.75f1 Built-in pipeline.

D. Unity Enlighten Realtime GI:
- Built-in has engine support but relies on precomputed surface clusters/visibility and GI-aware meshes/lightmaps. Valheim permits arbitrary dynamic construction, so retrofitting baked Enlighten to user homes is not a credible no-precompute WPR drop-in. Enlighten also has latency/cost and is unsuitable for rapidly flickering effects.

## Desired constraints and measurement
- Keep baseline installed, change no existing fire intensity/shadow position or shapes.
- No per-frame C# Update/LateUpdate/FixedUpdate/coroutines/polling. Reuse WPR's existing registration, invalidation and deferred shader pass only.
- Test 2 outcomes separately: (1) true GI visibility quality with zero through-wall leaks, (2) GPU/CPU ms in crowded chest/workbench/fire bases at 4K. If adding more than ~0.5–1ms initially, sharply reduce surfel count/screen resolution; no unconditional 96-slot * 32-surfels per pixel loops.
- Progressive independent build: baseline saved; isolated one-fire surfel generation, single-bounce contribution A/B; add bounce visibility before release; configurable toggle OFF by default while experimental; add profiling and compatibility. No GI code deployed as of this note.

## Public references
Unity Built-in Deferred GBuffer: https://docs.unity.com/en-us/engine/6000.0/manual/render-pipelines/built-in-render-pipeline/built-in-rendering-paths/render-tech-deferred-shading
Realtime GI uses precomputed surfaces: https://docs.unity.com/en-us/engine/6000.0/manual/lighting-overview/direct-and-indirect-lighting/lightmapping/realtime-gi-using-enlighten/enlighten
VPO indirect renderer CPU/GPU tradeoffs: https://github.com/ontrigger/ValheimPerformanceOptimizations
RSM GI basics: https://doc.babylonjs.com/features/featuresDeepDive/lights/rsmgi
SSGI screen visibility limitations: https://dev.epicgames.com/documentation/en-us/unreal-engine/global-illumination?application_version=4.27
Unity later Surface Cache GI dynamic construction/lighting preview: https://discussions.unity.com/t/surface-cache-gi-preview/1720494
