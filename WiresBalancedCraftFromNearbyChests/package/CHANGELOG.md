## 0.1.13
- Fixed: chest links could still turn green. The game's standard connection effect couldn't be found by name, so the mod fell back to whichever station upgrade happened to be loaded first, which could be a galdr table's green one. It now always takes the standard effect from a workbench or forge upgrade.

## 0.1.12
- Fixed: connection beams could turn green (the galdr table's effect was picked at random). All chest links now use the game's standard connection effect in its own colours, galdr table links included; the game's own upgrade connections are unchanged.
- Changed: no more separate background timer for what you're looking at; connections react to the game's own hover check.
