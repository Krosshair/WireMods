# Wire's Balanced Craft From Nearby Chests — native prefab colors, 2026-11-09

## User goal
Connection particles should look exactly like the game's own (the vanilla orange-yellow seen at the forge / bellows), not a forced colour. The forced golden yellow from `YELLOW_CONNECTION_WORKLOG_20261109.md` came out flat "piss yellow". Before the accidental green, the mod never touched particle colours.

## Cause (source-confirmed)
- Green came from prefab selection: `FindGenericConnectionPrefab()` used to take the first loaded StationExtension's prefab, which could be `vfx_ExtensionConnection_mage` (green HDR emission in `extensionconnection_mage.mat`). That selection was already fixed to prefer `vfx_ExtensionConnection` (both prefabs confirmed in `ValheimSOURCE\ValheimProject\ExportedProject\Assets\Effects\`).
- With the right prefab, the extra recolour (`ConnectionParticleSafety.ForceYellow`: start colour, colour over lifetime, colour by speed, trails, and an HDR `_EmissionColor` MaterialPropertyBlock) was unnecessary and replaced the native look.

## Change
- `src/ConnectionParticleSafety.cs`: removed the `ForceYellow` call and its helpers (`LinkYellow`, `YellowMaterialBlock`, `Yellow`, `YellowGradient`). Colours now come from the prefab unchanged.
- `src/ConnectionVisuals.cs` (user request: galdr links yellow too, vanilla upgrade links untouched): removed `FindConnectionPrefab(station)` (it took the station's own extension effect, green for a galdr table); both chest-to-station beams now use `FindGenericConnectionPrefab()` = `vfx_ExtensionConnection`. The game's own extension-to-station connections are not drawn by this mod and keep their own effects.
- The velocity-axis normalisation in the same method is unchanged.
- No other files, no version/README/manifest change. Native hover change from the previous worklog kept.

## Build / install
- net472 Release, 0 errors, 0 warnings.
- Previous installed DLL SHA256 `8BA412E8F380FE90F5BCC4F01FE71CF2CD26B4AEADE1371CD1B36ABA4A0581DB` (matched backup before replacing).
- New installed DLL SHA256 `47923EE7745EBA381BC2FD15DF805FF46E7A6A7D98C7AD247005030289A14FB6` (both changes).

## Rollback
`backup\NATIVE_PREFAB_COLOR_20261109\Restore.cmd` (previous DLL, game closed) and `RestoreSource.cmd` (previous source file).

## Needs in-game test
- Generic chest links and station links (workbench, forge) should match the vanilla extension colour.
- Galdr table chest links yellow (standard effect); the galdr table's own upgrade connections still green as in vanilla.
