# Wire's Enemy HUD

Valheim's own enemy health bars, finished. Weakness icons, a stagger bar, HP numbers and smarter visibility, drawn with the game's own art, fonts and colors, so it looks like it shipped with the game.

![Health bar features](https://raw.githubusercontent.com/Krosshair/WireMods/main/WiresEnemyHUD/docs/health-bar-legend.jpg)

## Weakness icons

![Damage type icons](https://raw.githubusercontent.com/Krosshair/WireMods/main/WiresEnemyHUD/docs/damage-icons-legend.jpg)

* What an enemy is weak to, at the end of its star row. Resistances follow in faint gray.
* One shape and color per damage type, built like the level stars.
* Bosses show weaknesses left of their name and resistances to the right.
* Read from each creature's real damage modifiers, so modded creatures work too.

## Stagger bar

* A stagger meter under the health bar, styled after your own.
* The frame flashes white when an enemy is staggered.
* While staggered, a yellow layer drains over the exact window your hits deal double damage.
* Hidden on enemies that can't be staggered. Synced in multiplayer.

## Health bars

* Double-thickness bars for enemies and players, same width as vanilla.
* HP as current, current/max or percent, on enemies, bosses and players.
* Boss HP follows the health fill and never runs off the bar. A DPS counter sits under it.
* See-through backgrounds, like your own HUD bars.
* Friendly creatures get their own bar color.
* A white flash on the frame when a backstab or a hit on a staggered enemy lands.

## Awareness and visibility

* The name color shows whether an enemy noticed you or is hunting you. Or use the vanilla icons if you want, or nothing, if you're a masochist.
* Hitting an enemy shows its bar for a few seconds, at any distance.
* Bars stay on screen: a troll's bar slides along the top edge instead of vanishing.
* Other players' nameplates fade until you aim at them.

## Built on the vanilla HUD

* Nothing is replaced: the game's own bars, frames, fonts and stars are used and extended.
* Mods with their own star visuals keep them.
* Light on performance. You'd be surprised how little the game tells you about stagger haha.

## Make it yours

* Presets: Author's Choice, or Vanilla as a clean starting point.
* Everything can be changed or turned off in the in-game Configuration Manager (I use the one by Shudnal).
* A hex color option for every color the HUD uses, vanilla ones included. Not recommended, but you can if you want to.

## Disclaimer: the code is AI assisted since I barely know C#. Not fully AI nor human. No AI image generation was used.
## https://ko-fi.com/krosshair

Thank you, darthvladi, for making HP Barheim. It motivates me to be better. You're awesome btw <3
