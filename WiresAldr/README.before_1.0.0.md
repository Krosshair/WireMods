# Wire's Aldr

*Aldr* is Old Norse for age or lifetime. The mod combines Valheim's evolving world, stronger biome exploration XP, challenging skill practice, wild resource yields, and fishing in one plugin.

## Main gameplay
- **Explore for XP:** Your **current biome** supplies most of the extra XP. Previously defeated Forsaken provide a smaller permanent +20% contribution of their progression curve. Farming weak early biomes is far less effective than progressing in dangerous territory.
- **All 24 vanilla skills:** Existing levels, vanilla skill HUD, save data and XP requirements remain unchanged. New weapon types get catch-up XP, and contextual actions (successful risky parries, actual perfect dodges, combos, moving-target hits, nontrivial crafting, etc.) receive modest extra XP.
- **Creature stars:** More native starred enemies after their own Forsaken, with diminishing later-boss contributions. No raid, boss health/damage or star cap changes.
- **Wild forage:** Up to +25 percentage points initial extra-harvest chance from home Forsaken with diminishing later contributions. Location-specific forest mushroom and Swamp thistle rewards; vanilla Farming chance preserved, default combined cap 80%.
- **Natural resource yields:** Native wood, stone, ore and higher-tier materials earn stochastic extra original drops as bosses are defeated. Source filtering prevents bonus loot from unrelated enemies or structures.
- **Fishing:** Hooked fish quality and biome affect Fishing XP. Successful rod catches can yield additional same-type/same-quality native fish after biome progress. Ground pickups don't qualify.
- **Multiplayer:** Same mod version on all clients and server. Game settings are server-synchronized; player-based Raids mode follows individual boss flags.

## Diagnostics for normal play

**v0.3.2 defaults to diagnostics OFF** for normal play. Enable it only while collecting an issue report. The existing diagnostic configuration is local and is not server-synchronized. Smelters and gameplay physics are unaffected.

Diagnostic log:
`BepInEx/Aldr-Diagnostics.log`

It is a separate readable text file. It is appended across sessions with a dated session divider. If the log grows above 2 MB, the previous file is rotated to `Aldr-Diagnostics.log.previous.log`.

### Optional debug test: stay at your base
1. Set `09 - Diagnostics / Enable diagnostics = true` in the existing BepInEx config, then start the game and load your existing world.
2. Stand near a loaded smelter, furnace, kiln or blast furnace. Put in ore and fuel if it is idle. Let it process a few items.
3. Walk around, craft something or cook if you feel like it. No need to cheat, fight bosses or start a new world.
4. Quit Valheim normally, then send `Aldr-Diagnostics.log` to inspect.

### Reading the log
- `SESSION`, `FEATURES`, `CONFIG`: correct mod version and options loaded.
- `PATCH_OK` / `PATCH_MISSING`: whether individual Harmony hooks were registered. **Registration is not proof of behavior.**
- `WORLD_READY`: the local biome, vanilla defeated-boss progression flags and expected baseline biome XP multiplier.
- `SMELTER_QUEUED`, `SMELTER_FUEL`, `SMELTER_CONVERTED`, `SMELTER_OUTPUT`: real observed smelting events, without modifying smelter output.
- `XP_EVENT`: actual vanilla skill-XP events, recording old and modified XP values, skill, biome, and level.
- `FORAGE_ROLL`, `NATURAL_DROPS`, `STAR_ROLL`, `FISH_ELIGIBLE`, `FISH_CAUGHT`, `PARRY_BONUS`, `DODGE_BONUS`, `RECIPE_XP`: recorded only if those actions really take place.
- `SUMMARY`: aggregate observations after a few batches and at shutdown. Mechanics not triggered by the session are marked untested, never falsely reported as working.

If you only stand around while smelting, the log can check startup, gameplay patch registration, world flags, smelter observation and any incidental XP. **It cannot prove combat, fishing or harvesting bonuses without those actions happening.** On a dedicated multiplayer server, the authoritative smelter events may be recorded on the server owner's copy of the log instead of the player's client.

Logs are throttled to the first five relevant events and then every 20th event per category, while XP samples are capped at the first three, tenth and every 50th event per skill. No per-frame debug loops. Diagnostic strings are created only while enabled, and writes are buffered, flushed at checkpoints and on normal shutdown. Debugging still has a small cost while enabled.

After a diagnostic session, change back to:
`BepInEx/config/wire.valheim.heimvoxtr.cfg` -> `09 - Diagnostics / Enable diagnostics = false`.

## Installation and upgrade
This package replaces earlier **Wire's Living Progression 0.3.0** and **Heimvöxtr** with one `WiresAldr.dll`. The old plugin GUID stays `wire.valheim.heimvoxtr` on purpose, so the same saved config and world data are respected. Don't enable another dynamic XP DLL at the same time.

No added currencies, no replacement vanilla skill values, no new persistent save keys. Recipe familiarity uses a separate small local config-side history text file, as in 0.3.0.

## Limitations
The actual v0.3.1 session verified loading, registered hooks, silver smelting and normal Run, Jump, Crafting and Dodge XP calculations. Combat, fish, resource and star-specific bonuses still require relevant gameplay to verify. The 0.3.2 refactor itself has passed build and pure calculation regression tests, but not a new live Valheim session. Logging is opt-in; there are no new per-frame monitoring loops.

ServerSync by Tykea (MIT license included).
