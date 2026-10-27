# Valheim Wire Patches

Fixes for vanilla Valheim bugs I've found. No gameplay changes.

This mod is meant to be temporary. The fixes are here so people can have them now, and I'm hoping they get added to [Valheim Community Patch](https://thunderstore.io/c/valheim/p/MidnightMods/ValheimCommunityPatch/) ([GitHub](https://github.com/MidnightsFX/Valheim-Community-Patch)).

Source code: https://github.com/Krosshair/WireMods/tree/main/ValheimWirePatches

Every fix has its own setting, all on by default. Client-side.

## Fixes

### Double click sound

Inventory tabs and buttons (Craft, Upgrade, Skills, Trophies, Achievements...) play the click sound twice. More noticeable when the game is lagging, like at a big base.

Cause: `InventoryGui.SetActiveGroup` plays `m_setActiveGroupEffects` (the sound for switching panels with a controller's bumpers), and the button plays its own `ButtonSfx` click in the same frame.

Fix: when `SetActiveGroup` has played its sound this frame, the button's own click is skipped.

### Lighting breaking after teleporting

After going through a portal, a dungeon entrance or exit, or loading in, the lighting looks right, then breaks (flat, orange, like everything is lit by the sky) and comes back a few seconds later in a wave spreading out from you.

Cause: lighting and reflections come from two realtime reflection probes (`ReflectionUpdate`), re-rendered one at a time every 3 seconds where you stand. Anywhere outside both of them falls back to the skybox, which is the broken look. Several things leave you outside both after a teleport:

* The probes are children of an object that follows the player. When a teleport ends, that object jumps to the destination, and a probe that was just rendered at the destination jumps along with it, thousands of meters away. Once the other probe gets re-rendered, nothing covers you until it has grown back.
* Probes keep being rendered during the teleport, before the destination has loaded.
* Short teleports (dungeon doors) end in the same physics step as the player moves, before the reference position (where probes are rendered) has caught up, so the arrival render happens back at the door.

Fix: the probes no longer follow the player (their position is set on every render anyway). No probe renders during a teleport. On arrival one renders where the player stands and shows at once, and another once the distant terrain is rebuilt for the new place.

### Lighting stuck on lightning flashes

If a probe renders during a lightning flash, the flash stays baked into the lighting until the next one.

Fix: a render that would land during a flash waits until the flash is over.

## Performance

Nothing runs every frame. The fixes only act when you click a button, teleport, spawn, or when lightning flashes, and during a teleport the game renders fewer probes than it normally would.



Disclaimer: the code is AI-assisted since I barely know C#. Not fully AI nor human.



https://ko-fi.com/krosshair
