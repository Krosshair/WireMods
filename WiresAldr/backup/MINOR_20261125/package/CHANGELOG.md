## 1.3.1
- The Aldr Compendium now shows each discovered biome's current maximum creature stars, including the extra star possible at night and the additional star for group leaders.
- Durability: stronger incoming hits cause more wear, shared randomly between equipped armor pieces; capes take very little. The total is based on the hit's share of your maximum health.
- Durability: blocking wears down the shield or weapon that actually blocked, based on the incoming hit's strength.
- Durability: weapons and tools keep their usual wear per use, varying only slightly with attack strength and a little randomness (about 5% either side of normal).
- Fixed: group-leader tracking avoids identifying an unrelated creature as the one just spawned. Without Wire's AI, the first group member still receives the star.
- Fixed: in player-based progression worlds, creature spawning uses a nearby player's defeated bosses when calculating native star limits and chances.
- Diagnostics: added checks for all durability hooks.
## 1.3.0
- A hit on a staggered enemy (a crit) gives twice the weapon XP.
- A hit on a weakness gives XP multiplied by how much more damage it did (1.5x on weak, 2x on very weak). A crit on a weakness multiplies both.
- A swing that hits several enemies gives XP for each, scaled by the share of damage each hit got (swings that split their damage give less per enemy; ones that don't, like a sword's primary attack, give full XP for each).
- The small bonus for hitting more than one enemy (+3% each) is replaced by the above.
## 1.2.1
- Creatures with more stars than the game has a look for (3 stars and up) now look the part: each star past the game's last one makes them as much bigger and shifts their colour as much as that last star did, and they keep its extra parts. Before, they looked like plain 0-star creatures.
- A biome's star limit is now soft at night: one more star can roll, with its own roll like every star (not in dungeons).
- Group leaders: the leader of a group of two or more has one star more than it rolled. With Wire's AI (0.1.2 or newer) it's the pack's own leader, otherwise the first one spawned.

## 1.2.0
- Higher star levels as you conquer the world: a biome's creatures can roll one more star for its own boss and for every boss after it. The Black Forest goes up to 3 stars after the Elder and 4 after Bonemass; the Meadows reach 10 after the Deep North's boss. Every extra star needs its own roll, so the higher ones stay rare. Only creatures native to the biome, not raids or fish, and a location's own level limits still apply.
