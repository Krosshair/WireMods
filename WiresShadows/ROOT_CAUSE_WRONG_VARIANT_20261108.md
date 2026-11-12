# November 8 all-point-lights-no-shadows regression: verified build/variant mismatch

## Proven root cause of deployment error
1. The actual, pre-first-fix Enngard WLO DLL backed up at backup/geometry_invalidation_20261108/WiresLightOcclusion_live_before.dll has SHA256 B7D76B1565462FF373E8D1472F9FCE45A311858EFAB901D255AABEC5D0724A61 and size 92,672 bytes, November 7.
2. It matches stage/WiresLightOcclusion_base.dll **byte-for-byte**. The source tree for this binary is src_base, NOT src. Building unchanged src_base on November 8 produced another 92,672-byte DLL with zero warnings/errors.
3. The first change on November 8 edited and built src/WiresLightOcclusion.csproj, yielding 175,104-byte binary (SHA256 8CDE6EB5DF095CE98313DB84A6853C3A474FE43EE50F8FD111FABA0B4EC5A792), replacing the previously installed src_base binary. The full binary has substantially different classes and requires a DIFFERENT shader bundle (src full counterpart staged bundle 276,902 bytes; the live/base shader bundle is 73,126 bytes). The live shader bundle was never updated when full DLL was copied; this was the fundamental incompatible binary/shader variant mismatch.
4. The BepInEx log reports the game's rebuilt shader being loaded, so missing-shader initialization was not the failure. Rather the in-game user observed light circles with no shadows, while full variant's [WLO shadow-map] reported 63/63 valid faces and many OwnPass casters. This is consistent with the full/native shadow pipeline not matching the base shader bundle.
5. The newest debug report [native-depth] INACTIVE at 18:41:06 happened immediately after 'Local player destroyed' and world shutdown, NOT while playing. Earlier interpretation of that log as the root cause was wrong.

## Resolution deployed
- Correct original src_base was used, backed up first at backup/actual_base_fix_20261108/ShadowCache_original.cs. No full source src code was installed. Changed only src_base/ShadowCache.cs, PieceChanged:
  - Renderer bounding boxes (excluding ParticleSystemRenderer and ShadowCastingMode.Off) detect long iron beams/bars rather than piece pivot only.
  - Never abort an in-progress six-face shadow map.
  - Never push out DirtyAt on a map already queued for rendering (avoids starvation with bursts of Piece.Awake).
  - Only WakeRenders if something is dirtied.
- Compiled corrected **base** DLL, 93,184 bytes, zero warnings/errors. Installed in Enngard while Valheim closed; SHA256 418BB5EFB2909F3954F51AFF1DBF7F026FA75F1DCE8A053C5C60BCB78C6A2CDA. Verified matching build and installed hashes.
- Critically, retained EXACT live/base shader bundle 'wirelightocclusion', SHA256 65E65AD40B30529A733139EC01629148326C2FB59F3C63EA66262B422EB311DC, unchanged.
- Config master enabled, cached shadows true, native comparison true, vanilla shadow drawing true. No fallback setting.
- Exact known-working November 7 base binary preserved backup/actual_base_fix_20261108/KNOWN_WORKING_base.dll; Restore_Known_Working.cmd checks Valheim is closed and restores it.
- Full variant latest source remains in src as a separate project; no migration of the incompatible full-native staging or sconce experiment into base. Do not merge these wholesale without a rebuild of both DLL + matching shader bundle and controlled A/B validation.

## Status
Code/config artifact pairing corrected and first actual source fix implemented. **Visual confirmation pending**: test the same room. If user still sees circles, restore exact known-working base with Restore_Known_Working.cmd to distinguish geometry patch from pre-existing configuration/other-mod issue. If visuals return, port sconce improvements individually to src_base (not src) and test.
