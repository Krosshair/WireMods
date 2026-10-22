# Wire's Stand Swap â€” 0.3.1

Place, retrieve and equip gear from Valheim item stands and armor stands without opening the inventory.

## Controls

| Input | Action |
| --- | --- |
| Tap E | Normal Valheim interaction, including taking a displayed item |
| Hold E briefly (0.22 seconds by default) | Smart stand action: equip / attach / swap currently held item; equip full armor stand when hands are empty |
| Shift + E (single press) | Immediately swap / equip ONLY the specific armor-stand piece under the reticle |
| Shift + E on an item stand | Immediately perform the smart item-stand action |
| R | Normal Valheim hide/show equipment, never hijacked by the mod |
| Alt + E | Normal Valheim alternate interaction (e.g. item-stand orientation) |

Both main interaction key and focused modifier are configurable in **1- Controls**. Hold E threshold is adjustable from 0.12 to 0.65 seconds. For another main key, use Modifier + that key for focused swaps.

## On-screen prompts

The hover UI describes the actual available actions:

- Equipped item stand: Hold E to swap held equipment or take and equip; Shift+E for immediate use
- Empty item stand, equipped item in hand: Hold E to attach; Shift+E for immediate attach
- Empty stand and empty hands: no custom swap prompt; standard hotbar attachment instruction remains
- Armor-stand equipment slot: Hold E for the smart/full outfit operation, Shift+E for the exact hovered piece, with ordinary tap E available for standard item removal
- Unavailable / non-equippable display: no false promise of a swap

"Nothing to swap" is displayed when requesting a swap that has no valid gear.

## Safety / design

- The focused swap requires a **real armor-stand interaction switch**. If your reticle is on generic stand geometry, it does nothing to the outfit and asks you to aim at a specific piece.
- With held equipment: empty compatible stands attach the held item, while compatible occupied slots exchange it for their displayed equipment.
- With hands empty: an item stand equips its displayed item, while an armor stand can equip the full loadout including body armor, weapons and shields.
- An entirely unequipped character can take/equip an outfit; normal inventory-space limits apply.
- The game handles normal equipping animations and delays (no instant equipment effect).
- Stand transaction validates ownership and item contents and rolls back when a swap fails.
- On each frame, input code only checks key states/timing; no scene or inventory polling is added. Stand switch IDs and equipment types are cached.

## Notes

Version 0.3.1 replaces the 0.3.0 hold-R feature, which clashed with Valheim's hide-equipment command. It also rebuilds the hover prompts. This is compiled and source-checked; **in-game interaction validation is still needed** before it can be considered bug-free.