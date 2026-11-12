# Pointlight Rework: globally exclude self-casters only for WLO-added shadow lights

## User-defined rule
- Lights that originally cast shadows in vanilla MUST preserve authored self-shadowing, geometry, and near clipping values. Do not infer from prefab names.
- Lights that did not originally cast shadows, and only cast them because of WLO, should never cast a shadow on their own emitting fixture. They MUST still cast shadows from ALL other nearby geometry.
- No broad minimum distance to prevent self-shadow: do explicit owner geometry exclusion instead; keep only 0.05 m minimum projection near clip.

## Actual project and build
- SOURCE: src_base/ShadowCache.cs and src_base/WiresLightOcclusion.csproj, matching the previously working 92KB variant (NOT full src variant).
- Exact existing shader bundle unchanged SHA256 65E65AD40B30529A733139EC01629148326C2FB59F3C63EA66262B422EB311DC.

## Implementation
- OwnShades(t) = t.OriginalShadowLod || t.OriginalShadows != LightShadows.None; removed !t.Tiny exception so even tiny vanilla-shadowed sources aren't accidentally classified as WLO-added.
- NearPlane(t) = OwnShades(t) ? max(.05f, t.OriginalShadowNearPlane) : .05f. Uses **original** value rather than potentially mutated current Light.shadowNearPlane; WLO-added lights get no special broad source margin.
- FindCasters already hides owner hierarchy for !OwnShades sources in native replacement camera pass (t.Hide). Added hierarchy-based exclusion for t.Sheets (two-sided sheets), t.Explicit + matching t.ExplicitSub (untagged material direct draw), and t.Show (shadow-only objects). This prevents bypassing the original exclusion through independent command-buffer passes.
- Nearby scene geometry untouched; collision proxy pass already excludes own hierarchy. No new Update, coroutine, polling, or recurring performance work; owner-filter runs only on static six-face recache.
- Previous iron-bar bounds-aware invalidation from corrected src_base retained.

## Verification and backups
- Built RELEASE src_base with 0 errors / 0 warnings. Live DLL SHA256 81408E43E6FA6F1A0F111DCE763C461D406CBD9FEBEEBDAF14193D6F570DE326, 93,696 bytes.
- Backup src_base source, previous DLL and existing shader bundle: backup/global_nonvanilla_self_exclusion_20261108.
- Restore.cmd restores previous DLL while Valheim is closed.
- No public release, no shader changes; visual verification of multiple vanilla and WLO-added lights still pending.

## Risks to check in game
- Lights with vanilla shadows (hearths, etc.) must preserve authored near-plane self-shadowing.
- WLO-added wall torches / lanterns / jack-o-turnips should not shade their own fixture but should correctly shade nearby architecture within former near margin.
- A light embedded within a wall still can't cast useful geometry depth inside the 5 cm near clip; this is projection geometry, not a self-shadow avoidance rule.
- Possible one-frame shadow-map refresh when changing cached invalidation is existing behavior.
