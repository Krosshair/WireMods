## 0.1.9

- Sorting supports every available inventory row, while leaving Equipment and Quick Slots' own slots untouched.
- The inventory pouch is anchored midway between armor and weight, including with inventory upgrades.
- Favorited items now prefer empty favorited inventory slots when picked up or added automatically.

## 0.1.8

- Sorting a container sorts your favorited items too, but they always come first, so your favorites gather together at the start of the container. In your inventory, favorited items and slots still stay where they are.

## 0.1.7

- Items can be favorited in containers too: hold the favorite key and left click. Favorited slots stay exclusive to your inventory.
- A favorited item keeps its frame while you hold it on the cursor.

## 0.1.6

- Favorited items stay where they are when you sort a container too.
- The pouch's rings and tooltip update right away when anything changes while you hover it, including containers coming into or out of range as you walk. No need to hover off and back on.

## 0.1.5

- UI sounds use the game's GUI audio channel and bypass environmental reverb.
- Carts, ships and other containers without their own opening sounds now play the vanilla chest sounds when used by Stackify.
- Favorited items and items in favorited slots cannot be dropped, including with MouseTweaks' Ctrl + click. They can still be moved between inventories.
- Favorited items show their inner frame in containers. Favorited slots remain exclusive to your inventory.
- Favorite frames now refresh when inventories change or slots are created, without per-frame UI hooks.

## 0.1.4

- Middle click sorting now only reacts to the click itself, nothing runs every frame anymore.

## 0.1.3

- Picked up items skip favorited slots and go to the next free slot. They still stack onto what's already in a favorited slot, favorited items can still go there, and if nothing else is free a favorited slot is used.

## 0.1.2

- did a oopsie dw bout it

## 0.1.1

- New controls: left click quick stacks, shift + left click restocks, middle click sorts by type, shift + middle click by weight. Every click can be changed in the new Controls section of the config.
- Shift + right click does everything at once: quick stack, restock, then sort your inventory and every container it just used.
- Middle click almost anywhere sorts again: the top of the screen sorts your inventory, the bottom an open container.
- Click your pouch while holding an item to quick stack every stack of it, or a container's pouch to top up your stacks of it from that container. A pouch opens when it can do something with it.
- The pouch opens for a moment whenever it quick stacks or restocks something.
- The pouch no longer has a drop shadow, like the armor and weight icons next to it.
- Hovering the pouch shows what it would do: a yellow ring on stacks a quick stack would take, a blue inner ring on stacks a restock would top up.
- Blue sparks for a restock.
- The tooltip says how many containers a quick stack or restock would use, and how much weight it would take off or add.
- Restocking never takes you past what you can carry.
- The inventory pouch always works with the open container and the containers around you; the container's pouch is for that container. The "With a chest open" settings are gone.
- Every click gives the empty-bag rustle right away, with the action's own sound after it.
- The yellow messages are gone, the sounds say enough.
- Hand cursor over the pouch with Wire's Native Cursor Support 0.3.3.
- Everything says container instead of chest, since it works with every kind. The Nearby Chests settings are now Nearby Containers and keep your values.
- Tooltips say which pouch is which: "Stackify" for yours, "Stackify (Container)" for a container's, which only works with that container.

## 0.1.0

First release: quick stack, restock and sort from a pouch next to your armor and weight, with favorites.
