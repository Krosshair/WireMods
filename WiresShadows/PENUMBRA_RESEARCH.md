# WPR Penumbra & Sun Shadows — Research Notes
Date: 2026-11-07. Status: RESEARCH ONLY; NO PENUMBRA PROTOTYPE BUILT. Target: Valheim Built-in Deferred / Unity 6000.0.75f1.

## Verified starting point
- All experimental fire-shadow contrast, widened PCF and edge-only breathing have been removed from the live Enngard version. Flames have VANILLA brightness and zero positional LightFlicker jitter; real moving objects still cast moving shadows. The atomic static-map refresh fix is preserved.
- Reverted DLL SHA256 B7D76B1565462FF373E8D1472F9FCE45A311858EFAB901D255AABEC5D0724A61.
- Reverted bundle SHA256 65E65AD40B30529A733139EC01629148326C2FB59F3C63EA66262B422EB311DC.
- C# ShadowCache.cs source SHA256 E3065B6741810026D53643CDF2321F04B814A742C0940FD06FA745F0036ABDED.
- Shader WireToonDeferredShading.shader source SHA256 FC4AA366153AFAF0A44ABED6CAA9BCD6D60D99F77DD2C1CE0D716491069226E1.
- Backup of rejected build: _removed/revert_fire_shadow_experiments_20261107.
- Known stray square/bottle shadows remain a separate, UNRESOLVED issue; do not claim fixed.

## User-requested penumbra goal
Physically motivated contact-hardening shadows for all possible types: WPR pointlights, Unity directional sunlight, and ideally spotlights. Near-caster shadows crisp; more separation produces softer edges. NO extra flame wobble or brightness animation. Build penumbra in a separate future experiment; do not pollute the stable baseline. No new C# per-frame Update/LateUpdate/FixedUpdate, coroutines, periodic polling or costly scene scans.

## PCSS mechanism
Percentage-Closer Soft Shadows is a well-established approximate algorithm:
(1) Search real shadow-map depth near the receiver ray for occluder (blocker) distances.
(2) Estimate the size of the soft edge using the separation between blocker and receiver, and the apparent angular size / physical size of the light.
(3) Filter the existing depth map via PCF using a corresponding bounded kernel.
Point/spot approximation: penumbraWidth ~ sourceRadius * max(0, receiverDepth - blockerDepth) / max(blockerDepth, epsilon).
Directional sunlight: growth proportional to blocker/receiver separation and the SUN'S ANGULAR DIAMETER (~0.53 degrees physically); map this into each cascade's light-space texels and cap radius. Do not apply the point inverse-depth formula to directional lights.

## Pointlight source assessment (WPR, inspected)
WPR renders static caster distances into RHalf cubemap arrays; near 1024 and far 256 with game quality. The static depth represents a front/back MIDPOINT, which might not equal the first actual surface: important near thin walls/planks. Current shader WireStatic + WireStaticBilinear does four comparisons per receiver; optional native comparison uses four hardware taps. Moving caster RGHalf front/back maps have corresponding WireMoving / WireMovingPCF / native comparison methods. An arbitrary enlarged four-tap footprint is NOT adequate full PCSS (we just reverted an aesthetic experiment like that).
Suggested first point prototype: fixed camera/light position, approximate 4-8 bounded blocker-depth search samples and 8-12 stable PCF comparisons, or small hybrid four-tap/9-tap quality levels if measured efficient; contact hard for separation near zero. Use conservative radius clamps, deterministic sample layout (NO temporal jitter), correct cubemap seams, slope bias and no cross-object accidental anchor guesses. Preserve WPR's direct drawing, sparse moving slots, native fallback and no added shadow map captures. Benchmark GPU milliseconds and verify caster attachment/zero stray shadows, incl. rugs, plank tops, terrain and characters.

## SUN / directional shadows: distinct integration
Valheim uses Unity Built-in Deferred. WPR currently overrides BuiltinShaderType.DeferredShading primarily to support its own slotted point-light cubemap maps; it does NOT own Unity's sunlight shadow-map generation.
Unity documents BuiltinShaderType.ScreenSpaceShadows for the cascaded directional screen-space shadow mask. Technical direction: clone the exact-compatible Unity 6000 built-in Hidden/Internal-ScreenSpaceShadows shader, first compile a NO-VISUAL-CHANGE replacement, and set only BuiltinShaderType.ScreenSpaceShadows using GraphicsSettings.SetCustomShader while preserving the original mode/shader for restore. Do not reuse an outdated 2016 source untested, and DO NOT copy HDRP shader code directly into Built-in rendering.
Retain the existing cascade selectors, world/light projection, reversed-Z compare, clip and fade, atlas bounds, stereo shader variants, normal bias and stable shadow edges. First verify raw sun shadow-map blocker depth can be sampled alongside hardware comparison: the built-in texture may not support a raw depth read in the same pass without alternate view, extra copy or explicit depth conversion. Validate D3D11 native shadow format before assuming PCSS is possible. Raw depth access is the KEY implementation unknown.
When blocker depth works, estimate light-space receiver/blocker separation and angular sun penumbra radius, normalize to current cascade resolution, clamp sampling inside current cascade atlas tile, avoid cascade boundary flicker. Preserve daytime/time-of-day sun rotation and sky changes. For spotlights, survey built-in spotlight shadowmap/SHADOWS_DEPTH receiver path separately; they cannot use WPR point cubemap filtering directly.

## Could sunlight optimization improve FPS?
Maybe, but NOT from adding PCSS alone: blocker-search and variable PCF usually cost MORE GPU pixel time than vanilla PCF. Distinguish (A) shadow-map GENERATION cost (culling/raster of sun casters in cascades) from (B) screen-space FILTER cost. The latter can be optimized via conservative early-out for regions demonstrably fully lit/fully shadowed, bounded samples near silhouettes, cheaper far cascades, hardware PCF and adaptive quality. Reusing existing sun mask and no extra fullscreen passes is important.
Generation optimization would require independent work such as improved caster culling, cascade resolution/split tuning or caching static geometry separate from moving casters. Cascades change whenever camera or rotating Valheim sun changes; naive old-map reuse causes projection swimming, ghosting or missed shadows. Cache invalidation and static/dynamic map compositing require substantial validation, not included in first penumbra build.
Microsoft cascaded-shadow guidance: stable cascade split intervals, texel-stabilized projections, avoid shimmer and excessive overlap. Measure shadow draw calls, GPU shadow render time, depth-masking cost before claiming any benefit.

## Future build sequence
A. Record BASELINE reproducible day/sun, forest, base, indoor torch, moving NPC, water and teleport benchmarks; GPU/CPU frame times and shadow map pass budget.
B. Prototype WPR-point contact-hardening using existing raw distance map only; test image parity/contact and bounded blocker depth plus PCF.
C. Prove ability to replace Unity directional ScreenSpaceShadows shader with NO visual changes; check vanilla cascades / revert.
D. Prove raw directional shadow-depth access and one-cascade PCSS, then full cascades, bias and temporal stability.
E. Extend to spotlights, if a separate native shadow receiver path allows it. Keep any SUN PERFORMANCE OPTIMIZATION a separate A/B measured project.
Test reset/disable/restoration and prioritize stable frame times and no new stray shadows.

## Sources reviewed
- NVIDIA GPU Gems 3, Chapter 8, PCSS blocker search and penumbra: https://developer.nvidia.com/gpugems/gpugems3/part-ii-light-and-shadows/chapter-8-summed-area-variance-shadow-maps
- Unity Scripting API, ScreenSpaceShadows: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/rendering/builtinshadertype/screenspaceshadows
- Archived Unity built-in source, Internal-ScreenSpaceShadows (old version, use as reference only): https://github.com/TwoTailsGames/Unity-Built-in-Shaders/blob/master/DefaultResourcesExtra/Internal-ScreenSpaceShadows.shader
- Unity HDRP docs, PCF vs PCSS point and directional (algorithm reference only): https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/shadows-soften.md
- Microsoft CSM engineering, cascades stability and filtering: https://learn.microsoft.com/en-us/windows/win32/dxtecharts/cascaded-shadow-maps

## 2026-11-08 implementation update — first combined point/sun prototype DEPLOYED
- WPR near-tier point PCSS implemented with 5 raw depth blocker samples + bounded 8 native/software PCF comparisons. Original static/moving shadow-source positions, actual caster transforms, distant-point PCF and all flame brightness rules preserved. No new CPU Update loop or shadow cubemap recapture. Source: LightRework/Assets/Shaders/WireToonDeferredShading.shader.
- Exact Unity 6000.0.75f1 built-in shader source downloaded from https://download.unity3d.com/download_unity/26349cd2a5c8/builtin_shaders-6000.0.75f1.zip (nested zip); SHA256 of original Internal-ScreenSpaceShadows.shader 005939B6CFC30DFE7245B0E3E1355F790DBE8AD229831B46CD9EA2A6DADBDDF0.
- First compiled an unmodified, renamed sun shader successfully. Then implemented D3D11-first-cascade PCSS with raw shadow depth .Load, average blocker, projection-correct metres, 0.265 degree angular radius and bounded 8-tap native PCF, leaving far cascades/hard shadows untouched. Source: LightRework/Assets/Shaders/WireSunScreenSpacePenumbra.shader.
- Added new C# plugin switch in General: "Sun contact-hardening shadows" (default true; live OFF restores original GraphicsSettings ScreenSpaceShadows mode/shader while WPR point remains active). Includes safe fallback if new shader is unsupported; WPR master Enabled OFF restores both original shaders. No added per-frame CPU logic.
- Current installed 8-shader asset bundle SHA256 86A3C2FE501DCED8B66A474379366786BCFECA789EEA3A995FEB8C0A38BD0290; Plugin DLL SHA256 A90E8A5F74AF38A2BB438E3A1208E514F060042E7F2ABC58A8A8FFC6242A8E6B. Unity 6000.0.75f1 validated 0 shader errors and 7 warnings (5 point+2 sun "potentially uninitialized" helper warnings), C# Release 0 errors/warnings. Game closed during deployment, installed hashes verified.
- ROLLBACK: _removed/sun_pcss_prototype_20261108 for restoring point-only PCSS, and _removed/point_pcss_prototype_20261108 for returning to pre-PCSS static-shadow baseline.
- STILL UNVERIFIED: actual game visuals, sun raw-depth read compatibility, sun shader being selected by renderer, near-caster contact and FPS. Do not claim sun optimization; PCSS introduces extra GPU lookups, particularly around mixed blockers. The persistent bottle/rug ghost-shadow bug remains separate.
