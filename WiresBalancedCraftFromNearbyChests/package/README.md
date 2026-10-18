# Wire's Balanced Craft From Nearby Chests

Craft, build, refuel and load machines using **specifically named nearby chests**. Unlike unrestricted craft-from-chests mods, it only uses storage assigned to the relevant station, tool or machine. Mod is designed to be a mix of those mods which I find too overpowered, and vanilla, which is way too restricting in my eyes.

## Getting started

1. **Rename a chest** to include a route keyword. For example:
   - `Workbench Materials` — workbench crafting
   - `Building Supplies` — hammer building
   - `Smelter Ore` — smelter input
   - `Fuel` — refillable fires and lights
   - `Kitchen Ingredients` — cauldron, food preparation table and mead cauldron
2. **Use the station, build tool or machine normally.** Materials come from your inventory first, then matching chests within range.
**Hover over a station, machine or named chest** to see its connections. Tool connections appear while that tool's build menu is open.

To rename a chest, use **Alt + Use**. If another compatible chest-renaming mod is installed, use that mod instead. My Little UI (by Shudnal) has Shift + E, for example.

## Chest routes

Matching looks for keywords anywhere in a chest's name. Each station, build tool and refill route has up to **three configurable keywords**.

Supported routes cover all crafting stations; **Hammer, Hoe, Cultivator and Serving Tray**; and fuel or input for kilns, smelters, cooking stations, ovens, fermenters, refineries and other supported machines.

**Shared keywords** serve several related routes: `Smelting`, `Kitchen`, `Crafting` (every station that raises the Crafting skill: workbench, forges, galdr table and more, never cooking), `Plains Processing`, `Frost Processing` and `Siege`. See the config for every individual keyword. If multiple matching chests are available, chest-title sorting determines which is used first.

## Multiplayer
Installing on the **server is optional**. If installed there, the server's configured route keywords sync to clients.

## Settings

Use BepInEx Configuration Manager or edit:

`BepInEx/config/wire.valheim.balancedcraftfromnearbychests.cfg`

Change the three aliases per route, shared keywords, chest-renaming preferences and **Show connections**.

[Connection visual examples](https://imgur.com/a/BN3TRma)

*AI-assisted code, not fully human or LLM. Donations: [ko-fi.com/krosshair](https://ko-fi.com/krosshair).*
