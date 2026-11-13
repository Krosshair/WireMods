## 1.1.11

- Fixed: a stutter on skill level-ups (with FortifySkillsRedux): finding the inventory's blue to tint the level searched every object in memory, and again on later level-ups if it found none. It only looks in the inventory now, once.

## 1.1.10

- Added contextual icons to hammer repair notifications using the actual building piece, with compact "Repaired" and "No repair needed" messages.
- Repair notifications now use yellow text.
- Snapping notifications now display the selected build piece's icon and only the snapping type, without the "Snapping:" prefix.
- Expanded Compact mode for common vanilla building and placement errors, including blocked placements, invalid locations, private areas, biome and terrain requirements.
- Individual repairs, unsuccessful repair attempts, and snapping-mode changes no longer merge into earlier identical notifications. Countable notifications, such as item pickups, retain their grouping.
- Existing equipment repair notifications also remain separate rather than being merged.
