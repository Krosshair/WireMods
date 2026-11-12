# Pointlight Rework — targeted cache invalidation fix, 2026-11-08

## Player observations
- Eitr refinery light changes its cached shadows late.
- Removing iron bars in front of refinery left square dotted shadow remnants; placing an iron bar possibly cleared them.
- Two opposing entrance sconces have historically shaded a gate asymmetrically and overly dark on one side.

## What was verified in code
- ShadowCache.PieceChanged previously checked only a changed piece's pivot against each light center; long iron bars/beams can affect a shadow while their pivot is outside the relevant light's reach.
- PieceChanged could mark Dirty but leave Face>=0 while an old six-face re-render was already in progress. That rebuild could complete and publish geometry captured before the placement/removal, then another rebuild later.
- Static map rebuilds are deliberately staged face-by-face and swapped atomically, so dirtying an in-progress map must cancel the stage before restarting.
- This does NOT establish that the refinery timing or asymmetric sconce shadow are caused by these same bugs; they remain to verify.

## Actual change
- Only ShadowCache.cs modified. On piece-change events, evaluate the full shadow-casting Renderer bounds (via a reused List<Renderer>, no per-frame allocation/scan) and invalidate lights reaching that geometry, not just the pivot.
- If a six-face map is partially rendered, abort its stage and reset its face index so the next refresh starts with current casters. Reset any partial gate-face queue as well.
- Preserve the existing 0.15-second settle period, static render budget, all moving shadow code and the native shader bundle. No new Update, coroutine, per-frame polling or changed shader.
- Enngard test DLL rebuilt in Release, 0 errors, one existing compiler warning CS0169 (OwnPass.cs unused field _ownProbes).
- Installed with Valheim closed. Hash SHA256: 8CDE6EB5DF095CE98313DB84A6853C3A474FE43EE50F8FD111FABA0B4EC5A792. Shader bundle untouched.
- Source before and prior installed DLL saved under backup/geometry_invalidation_20261108. Restore.cmd restores DLL with game closed.

## FarLands log check
- Enngard BepInEx/LogOutput.log, last modification 2026-11-08 15:18:50, ~8 MB.
- FarLands 1.2.0 loaded. 22 matching lines, 0 with error/warning/exception/failed text.
- Logs: far-world initialization completed in 72.3 seconds (land, trees, buildings). 23 tree types (20 drawn), 18 understorey types (17 drawn). Materials for distant buildings 76 custom far piece shaders, 7 native materials.
- AutoDetail oscillates repeatedly from GPU busy ~48–55% / terrain detail 2.0 / near land 2m to GPU busy ~86–90% / terrain detail 1.4 / near land 4m. May be adjustment instability; not evidence of a fault or a causally confirmed FPS issue.
- No FarLands exceptions found in those lines. Other mods or underlying runtime issues aren't excluded by this targeted search.

## What to test
1. Destroy and replace iron bars in front of the Eitr refinery. See whether old dotted blocks linger or disappear with one complete map update.
2. Observe refinery's shadow while light flickers. If it still visibly lags, trace its local light motion versus RestingPosition and shadow map capture next.
3. Reinstall the entrance gate if practical and inspect the two sconces from each side. Compare vanilla shadow mode versus cached to diagnose shadow caster/bias rather than guessing.
4. Keep this as a test build; no release requested or published. No claim of in-game success yet.

## Additional native shadow staging fix, installed 2026-11-08
- Investigated Eitr refinery delayed cached-shadow updates and unusual dark gate shadows from opposing sconces.
- CONFIRMED CODE ISSUE: OwnPass.DrawFaceVanilla and ShadowCache.CacheNativeFace wrote partial updates to the visible native comparison-depth cubemap while Tracked.Staged was true. Code reset _nativeFaceMasks on staged refresh, and UpdateGlobals disabled native shadows when t.Staged: this made a previously valid cached shadow vanish during six-face updates. Shader light center also changed to new center before map swap, yielding transient coordinate mismatch.
- CORRECTED: a per-tier 6-face native Shadowmap staging cube (lazily allocated, only as required); finished update swaps all six faces via Graphics.CopyTexture. Keep old _nativeFaceMasks and native active status while rebuilding. Track NativeStaged to prevent copying unused scratch data. Shader uses PublishedCenter while a staged rebuild is underway and switches center alongside finished depth map.
- Existing per-face rendering budget remains intact. No extra rays, no new Update/FixedUpdate or more frequent refresh. Native staging costs one 6-face comparison depth cubemap per tier if a staged update occurs (at near 1024px 24-bit depth roughly 24 MiB, far 256px ~1.5 MiB; actual GPU allocation implementation-dependent). Six face GPU copies once per completed refreshed map, not every frame.
- SCONCE SIDE-SPECIFIC DARKNESS: observed symptom NOT yet causally explained or claimed fixed; existing native comparison A/B toggle can help differentiate geometry/shadow bias from caching. Do not apply speculative proxy exclusion, because it would reintroduce light leaking through walls.
- Previous code and installed DLL backed up under backup/native_stage_20261108; Restore.cmd restores prior Enngard version while game is closed.
- Release build: 0 errors; 1 existing OwnPass.cs warning CS0169 for unused _ownProbes. Installed to Enngard while Valheim closed; matching SHA256 8F173B0FFB424C6A98A612641A1948898DBBD11FA2C8CBF017786234C2B6C516. No public release.
- Test in-game: eitr refinery shadow behavior on successive updates, iron-bar destruction/placement, sconce/gate asymmetry and shadow popping. If an issue persists, capture native/vanilla sconce A/B with exact light selected instead of changing general bias.

## Sconce near-plane hypothesis/test build (2026-11-08)
- User suspects the two sconces' asymmetric gate shadow is from a margin before they start casting rather than their actual point-light position.
- Confirmed implementation: ShadowCache.NearPlane uses max(0.05, Light.shadowNearPlane). Existing source comments explicitly cite stone around a sconce and 0.8m near plane. Both the native comparison shadow projection and the near-plane collider proxy logic use this same radius.
- Scoped experiment: when Tracked.Sconce && !OwnShades(t) (no vanilla self-shadow), use 0.05 m near plane for WLO cached and moving shadow calculations. Own sconce fixture was already excluded as a caster, so avoid a large empty sphere around its light source. Other light types retain authored values.
- Native 'Compare with game shadows' diagnostic explicitly retains OriginalShadowNearPlane so game comparison is not contaminated by WLO's sconce test.
- This is a hypothesis-driven test, not a verified visual fix; check for accidental shadows from nearby brackets, wall leakage, and both sides of the gate. Do not publish before in-game result.
- Build Release: 0 errors, one pre-existing unused _ownProbes warning. Installed test DLL in Enngard while game closed; SHA256 9B34F3993C6A8F82EF17347F08BC1940A551DE045C8B89A42CC013B951A5B57C. No shader rebuild and no per-frame additions.
- Previous source/DLL saved backup/sconce_near_plane_20261108; Restore.cmd restores previous DLL with game closed.

## Complete sconce fixture self-caster exclusion, 2026-11-08
- User asks for a complete exclusion of the sconce casting its own shadows instead of a broad starting margin.
- Existing FindCasters excludes the light's own Piece renderers in t.Hide; native OWN shadow caster list honored that filter, but t.Sheets and t.Explicit represent fallback/two-sided geometry drawn through a different pass, and could bypass self-exclusion.
- Updated FindCasters to remove renderers in the light's own Piece hierarchy from both t.Sheets and t.Explicit (with matching t.ExplicitSub indices). IMPORTANT: do NOT remove every member of t.Hide from t.Sheets because all valid two-sided sheets are deliberately in Hide to avoid double-rendering.
- All non-owned scene objects, including entrance wall, gate, posts and beams, remain valid shadow casters. The earlier 0.05 m sconce-specific near plane remains. Cannot set projection near clipping to literal zero; 5cm is a mathematical camera limit, not a broad self-shadow margin.
- Build 0 errors / 1 pre-existing unused _ownProbes warning; installed Enngard test DLL with game closed. SHA256 25648F1A5E09A91DD1D043C5ACE81F41BA5E205DA7A9209396D41F7C6A356F01.
- Backup before this change at backup/sconce_self_caster_20261108 (ShadowCache.cs + previous live DLL). No release package or shader changes; still requires in-game A/B to establish gate asymmetry cause.

## Critical global-shadow regression and rollback (2026-11-08 16:26)
- User screenshot showed all point lights illuminating in circular radii but no effective shadows after the latest native-stage and sconce changes.
- Enngard LogOutput.log ended at 16:26:10. It included native-depth repeatedly alternating ACTIVE (1 ready) and INACTIVE, ending with '[native-depth] INACTIVE: all-native comparison awaiting maps or disabled.' No WLO exceptions were logged. This supports shadow-map eligibility/masking being lost, not a light source failure, but the exact cause among the experimental edits is not conclusively isolated.
- IMMEDIATE ROLLBACK: with Valheim closed, restored BOTH src/ShadowCache.cs and src/OwnPass.cs AND the installed Enngard DLL from backup/native_stage_20261108, i.e. the build AFTER the first piece bounds invalidation fix but BEFORE native-stage rendering, sconce 5cm near-plane or fallback self-caster exclusion.
- Confirmed installed rollback DLL SHA256 8CDE6EB5DF095CE98313DB84A6853C3A474FE43EE50F8FD111FABA0B4EC5A792. Broken source and DLL preserved in backup/rollback_global_shadows_20261108 for postmortem; do not deploy them.
- The staging scheme was flawed: shader visibility uses native-face masks, stage flags and readiness checks; incorrect ordering/coupling can leave native-depth inactive with Unity shadow sources suppressed. Need test these independently and verify 6-face masks for both near/far tiers before attempting another replacement. User should verify restored shadows in game.
- DO NOT reintroduce any of these experiments without a controlled A/B and safety fallback.

## Fallback to existing non-native shadow maps after rollback also failed, 2026-11-08 16:29
- User confirmed screenshot STILL shows radius-only lights after restoring pre-staging geometry-only DLL. Recent LogOutput.log LastWriteTime 16:29:37, 201861 bytes shows native-depth FAR array allocated, ACTIVE (1 ready), NEAR array allocated, then INACTIVE. No crash/exception. Thus simply reverting recent source/DLL did not restore functional native shadowing.
- Current installed DLL remains SHA256 8CDE6EB5DF095CE98313DB84A6853C3A474FE43EE50F8FD111FABA0B4EC5A792; source matches this geometry-only build.
- With game closed, BACKED UP wire.valheim.lightocclusion.cfg to backup/rollback_global_shadows_20261108/lightocclusion_config_before.cfg, then changed only: 'Vanilla shadow drawing = false' and 'Native comparison for all cached lights = false'. Master Enabled and Cached shadows still true. This forces WLO's older alternative replacement-shader static depth maps (ShadowCache.DrawFace fallback), rather than the nonfunctional native Shadowmap comparison array. Other moving-shadow settings untouched.
- Not visually tested yet. If shadows return, the regression is isolated to new native-depth/own-pass machinery; if they don't, investigate geometry masks, shader bindings or other mod conflicts. This is a safe provisional fallback, not a proof root cause is fixed. Restoring config reverses it. Do not claim confirmed recovery until user reports.

## Latest native-pipeline repair test (2026-11-08, after user demanded a real fix)
- User requested repair from latest revision rather than old fallback. Backed up then restored latest source snapshot preserved in backup/rollback_global_shadows_20261108 (ShadowCache.cs + OwnPass.cs). This includes the preceding native-depth staging and sconce exclusion changes.
- ROOT CODE CAUSE OF UNLIT SHADOWS: the existing WireToonDeferredShading.shader explicitly sets staticLit=1 when _WireVanillaDepth > .5 and _WireNativeActive[slot]<=.5 (lines ~479-530). In old ShadowCache.cs UpdateGlobals a ready light became native-inactive whenever t.Staged=true and native-face mask was zeroed at first staged face. Any shadowed light in this transition therefore became a plain circle until native state returned. This was compounded by the rendering scheduler letting a ready light bypass staging when a peer owned that tier's single spare cube, directly overwriting published native-depth faces while their previous validity was still claimed.
- Latest revision already preserved old native mask/active state during staged recache, rendered to dedicated native-depth staging cube, copied six faces atomically, and kept PublishedCenter. Additional source repair: RenderDirty now defers ready dirty lights when the tier staging cube is busy; no direct overwrite of published depth allowed for ready recaches. Fixed in ShadowCache.cs only; OwnPass.cs latest restored.
- Diagnostic on native active->zero transition reports counts of occupied slots, ready slots, staged slots, owner mismatches, incomplete six-face masks, shader setting, native fail flag, RT readiness. Event-driven (only when global native state transitions); no new per-frame diagnostics.
- Build: succeeded 0 errors, one pre-existing warning CS0169 _ownProbes. Installed Enngard test DLL SHA256 B8FFEFE3387A2EC2A02EDE74F8831FC8D2E9FAA63536AA513BA6A3FCFAC93DD4; Valheim closed. Restored native comparison and vanilla shadow drawing to TRUE in Enngard cfg (master Enabled + Cached shadows remain TRUE).
- Previous DLL and config backed up in backup/native_root_repair_20261108, Restore.cmd restores both. No changes to shader bundle, release ZIP or published mod. NOT visually verified; must test in same room and inspect new reasoned log if shadows still missing.
- Important caveat: logs with 'native-depth inactive' alone do NOT prove why all lights remain unshadowed; active count also drops normally if all eligible slots are released/streamed. Need new transition-reason log to pinpoint if symptom persists. Previous failures require correct humility.

## 2026-11-08: Root repair starting from FIRST broken geometry change + latest improvements restored
- User confirmed the FIRST fix from before the FarLands grass-log query already broke all point-light shadows. The 2026-11-08 native-staging and sconce revisions were NOT the original onset.
- Exact initial regression found in ShadowCache.PieceChanged: added `if (t.Face >= 0) { cache.AbortStage(t); t.Face = -1; }`. This resets progress on every piece geometry event. During streaming, hundreds of Piece.Awake events repeatedly cancel six-face maps before they become Ready. The renderer subsequently has no usable cached shadow map, and the shader returns fully lit light circles. The original source (backup/geometry_invalidation_20261108/ShadowCache.cs) had no cancellation and simply marked t.Dirty=true, which RenderFace clears only at START, naturally scheduling a second rebuild after the active render completes. This is the real code flaw introduced by the FIRST patch; visual fix not yet verified.
- Recovered latest failed source from backup/rollback_latest_failed_20261108 (includes all native-depth staging, published-center alignment, staging queue guard, sconce-specific 5cm clip, comprehensive fixture exclusions, transition-reason logs).
- Surgical repair: retain bounds-aware caster invalidation, but never AbortStage or reset Face inside PieceChanged; set Dirty=true and DirtyAt=Time.time+settle so the current six-face job reaches completion and a second updated job occurs after. Reset door-face queue only when no full-map render is currently in progress.
- Static checks: no AbortStage/Face=-1 in PieceChanged; latest native-stage creation, six-face atomic swap, fixture exclusion, contention guard, reason logging all present.
- Build succeeded 0 errors, 1 preexisting OwnPass unused-field warning; installed to Enngard while game closed. DLL sha256 CAA65A751C55EA5BB1D176BC68B90239FFFB23B2F592943B5539A4791CEF6AA5.
- Reenabled native depth and vanilla shadow drawing settings (both true); cached shadows true. Original previous config and previously installed DLL/source backed up backup/root_geometry_repair_latest_20261108; Restore.cmd is available.
- No shader bundle modification or release publication. The graphics result still needs in-game validation at same room before declaring fully fixed. If still broken, use transition-reason diagnostics in next log to isolate incomplete masks, owner mismatch, RT failure etc, rather than bypassing rendering pipeline.

## 2026-11-08 18:15+ re-evaluation: no post-spawn steady-state logs
- User again reports all-light no-shadow regression after previous source fix. LogOutput.log last edited at 18:15:25 and WLO profiling only covers player=not spawned, with last native-depth lines FAR allocated, ACTIVE with 1 map ready, NEAR allocated. This log does NOT contain a native INACTIVE transition or any 10-second WLO report following actual player spawning, so previous inactive-shadow diagnosis was not supported for this particular run.
- Plugin loads custom deferred shader from adjacent bundle wirelightocclusion. Live bundle (7 Nov, 73126 bytes) differs from stage bundle (3 Nov, 276902 bytes). Bundle difference was NOT introduced by recent source-only patches, so not a proven regression cause; DO NOT blindly substitute bundles.
- Further verified starvation mechanism in ORIGINAL piece-change replacement: repeated Piece.Awake calls could keep re-arming t.DirtyAt=Time.time+settle even when already dirty; with many pieces streaming, initialization is postponed continually. Also the changed piece bounds gathered ParticleSystemRenderers, whose huge non-caster effects could invalidate many lights. Earlier AbortStage bug was removed, but these two remaining first-fix regressions were still present.
- Changed PieceChanged: exclude ParticleSystemRenderer from bounds used for static caster invalidation; if Dirty already true, NEVER re-arm settle timer. This allows initial map jobs and queued rebuilds to progress. When no refresh pending, schedule exactly one. Existing latest native staging, sconce exclusions and pipeline preserved. No additional frame hooks.
- Added diagnostic to existing 10-second stats: [WLO shadow-state] counts occupied and ready light slots, native 6-face masks, native active bits, owner mismatches, still rendering and dirty, and rendered static faces. Also log first 28 FinishMap events with actual casters and native validity. Only active with pre-existing Debug log=true; no per-frame instrumentation/scan added.
- Build 0 errors, one pre-existing CS0169 unused _ownProbes warning. Installed Enngard latest test DLL while Valheim closed SHA256 24049997F253FE0DAFEAC56FD606C048D6AE4AABFBC949D2B389009BC3C53721. No bundle edits. No release.
- Root of global no-shadow picture remains UNVERIFIED visually. User should load actual base world and remain at least 15 seconds after spawn, then inspect new [WLO shadow-state] and [WLO shadow-map] records. If ready nativeActive high yet no shadow, root is caster/depth/rendered shader path, NOT scheduling. If ready nativeActive low and pending faces/dirty high, scheduling/invalidations still starving. If casters=0, source geometry collection is broken.
