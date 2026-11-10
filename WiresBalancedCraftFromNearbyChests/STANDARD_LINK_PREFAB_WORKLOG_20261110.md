# Balanced Craft From Nearby Chests: links still green sometimes, 2026-11-10

## Report
After 0.1.12 the chest links can still be green (screenshot: hovering a reinforced chest in the kitchen).

## Cause (source)
`FindGenericConnectionPrefab()` asks `ZNetScene.GetPrefab("vfx_ExtensionConnection")` first. That effect isn't a scene prefab (it's only referenced by station upgrades), so the lookup returns null and the old fallback ran: the first loaded StationExtension's `m_connectionPrefab`, cached for the session. With a galdr table upgrade loaded first that is `vfx_ExtensionConnection_mage` (green). Exported project: 126 upgrades reference `vfx_ExtensionConnection`, 15 the `_mage` one.

## Change (src/ConnectionVisuals.cs)
Order is now: scene prefab by name; then the workbench/forge upgrade prefabs from the scene (`KnownExtensionPrefabs`), taking their effect only if it is named `vfx_ExtensionConnection`; then loaded upgrades, again only the standard one. If nothing standard exists, some other loaded effect is returned but not cached. New helper `IsStandardConnection`.

## State
Built 0 warnings, 0 errors. Installed in Enngard, SHA256 52DE8A5CBC905449C47675FAFAD0F92B25A6E70ADE12A3610BAA6F964D8AF734. Not tested in game. Version not bumped, changelog untouched.
Rollback: backup\STANDARD_LINK_PREFAB_20261110 (Restore.cmd, RestoreSource.cmd).

## Release prep 0.1.13 (user asked for the release command)
- src/Plugin.cs Version 0.1.13; package/CHANGELOG.md 0.1.13 entry. Builds 0 warnings.
- 0.1.13 build installed in Enngard after the game closed (log ended with "Steam manager on destroy").
- Green-link fix itself not yet confirmed in game by the user.
