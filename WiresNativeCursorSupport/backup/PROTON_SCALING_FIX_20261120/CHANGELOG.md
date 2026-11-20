# Changelog

## 0.4.1
- Fix: respect explicit desktop XCURSOR_SIZE and Linux cursor-size configuration, including when Valheim runs as a Windows process through Proton/Wine. X11 default cursor size no longer overrides user sizing.
- If a cursor theme contains only a smaller bitmap, resize it and its hotspot once during initialization. Windows native cursors are left untouched unless a desktop size is explicitly detected.
- No new per-frame hooks, polling, or window message interception. No changes to cursor-state selection.

## 0.4.0
- Performance: the cursor costs nothing while you play. It changes only when the game says something changed (you hover an item or a text box, pick an item up, start crafting, drag the map, a loading screen shows) instead of being checked every frame, and the game window's messages aren't intercepted any more. Your system's cursors are read once at startup.
- Removed: the hand over map pins. It needed checking every frame.

## 0.3.3
- Other mods can now ask for the hand cursor over their own buttons (Wire's Stackify uses it for its pouches).

## 0.3.2
- Refined dynamic cursor behaviour.
- Map panning now switches to Move only after an actual drag begins, not on mouse-down.
- Existing map pins use Hand while the rest of the map stays Arrow until dragged.

## 0.3.1
- Refined cursor behaviour.
