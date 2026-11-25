## 1.4.0
- New: Knockback. How far a hit pushes depends on how much of the target's maximum health it takes: 50% pushes as in vanilla, less below that, up to twice as far. For hits on you and on creatures. Fully blocked or absorbed hits don't push; the shove a block gives the attacker stays.
- New config layout: section 01 has a switch and a strength for every feature. The numbers behind them are in the sections after it and marked advanced. Your saved values carry over.
- New switches and strengths for what had none: location XP, catch-up, hit XP (every enemy hit, critical hits and weaknesses), sleep, higher star limits, the night star, group leaders, star looks, durability and knockback.
- New colours for creatures with more stars than Valheim has looks for: hand-picked for each creature, a different one for every star from 3 to 10. Before, each star repeated the change of the last one, so boars turned completely black and hares completely white. Creatures from other mods go round the colour wheel.
- Bodies are as big as the creature was: with its stars, Wire's AI's body size, and young ones (piglets and wolf cubs left full-grown bodies). Draugr and moose bodies were a bit too small even without stars.
- New strengths for Expedition, challenge XP, rewards, creature stars, foraging, materials and fishing.
- New advanced settings: armor wear and its cap, the cape's share, block wear and its cap; knockback on players, on creatures, and the share of health that pushes as in vanilla.
- Settings changed in-game are saved right away. Before, they could be lost.
- Fixed: the raid bonus no longer applies while Challenge XP is off.
- The Compendium also shows whether durability and knockback are on, and only mentions the night star and group leaders while they are.
- Code cleanup: the attack hooks are merged, so each swing and hit runs fewer of them; unused parts removed; the XP code split into three files.
- Diagnostics: added knockback and the new switches. Removed the startup line that mentioned the diagnostic file even when there was none.
- New icon, better README with images.

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
