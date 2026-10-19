# Changelog

## 0.3.0
- The weapons and tools shown on your character are now remembered per character across logouts and game restarts, the same way Helmet Hider remembers hidden helmets. Stored in BepInEx/config/wire.valheim.weaponmaster.shown.txt, written only when your character saves.
- The main menu character preview shows your remembered weapons too.
- New config option **Remember between sessions** (General, on by default) to turn this off.

## 0.2.1
- Added a global **Enabled** toggle for Weapon Master.
- Added individual toggles for the permanent-display categories: `backShield`, `backTool`, `backMelee`, `backTwohandedMelee`, `backAtgeir`, and `backBow`.
- Disabling a category only disables Weapon Master's permanent extra display for that category. Normal Valheim stowing remains unchanged.
- Added server-authoritative config synchronization so modded players use the server's Weapon Master settings when the server has the mod installed.
- Added dedicated-server support for synchronized config.

## 0.2.0
- Added multiplayer visibility for Weapon Master holsters between players running the mod.
- No server-side installation required.
