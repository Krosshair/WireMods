## 0.8.9

Fixed enemy damage trails when a hidden health bar is shown by the mod's damage/parry reveal or look-at conditions: the yellow slow bar keeps its normal delayed animation across visibility toggles, including the first hit that creates a nameplate. Reveal conditions now restore full opacity and remove low-opacity trail masking while visible. Vanilla health-bar behavior and opacity masking while faded remain unchanged.

Limited mode set to Limited no longer goes back to Highlight when you restart the game.

## 0.8.8

Player nameplate effects off now hides them on player nameplates completely, instead of switching players to the creature rule.

In the config, creature and player versions of the same setting now sit next to each other (HP numbers, double size bars).

## 0.8.7

Other status effects no longer check every nameplate each frame while nothing is shown: only when the shared data changes.

## 0.8.6

Other status effects: right of the health bar, the game's own icons for effects that change a creature or player itself (its resistances, speed, damage, stagger or armor), such as tarred or harpooned. Effects that don't, like cold or rested, aren't shown, except wet, since fire burns wet creatures faster. Shared in multiplayer like the others. New setting: Other status effects.

On players, other status effects show only what you can't tell from where they are or what they wear: rested, encumbered, their Forsaken power, wet, then cold or freezing, in that order. Gear effects, shelter and the like, and mead effects are left out. New setting: Player nameplate effects (on by default; off uses the creature rule).

Show far creatures you look at (new, on by default): a creature's bar also shows when you look at it from further than the game shows bars, and goes away when you look away.

Limited mode is now a choice: Off, Highlight (new default: the icons for the damage types your weapon in hand deals are a little bigger and brighter, every other icon still shows) or Limited (only those). Damage icons only on hover is now off by default. Existing settings carry over.

Item tooltips show each damage type's icon in front of its line, in your icon style and colors. New setting: Icons in item tooltips.

Damage type icons on player nameplates now sit the same distance below the health bar as on creatures, instead of a little too high.

The Vanilla preset now also turns off Resistances for player nameplates and the new settings above.

## 0.8.5

Damage type icons now follow a creature's real resistances instead of being baked-in, including what its status effects change: if an effect makes a creature weak or resistant to something, its icons show it, and go back when the effect ends. Updated only when an effect starts or ends, and shared in multiplayer so everyone sees the same icons.

Players get damage type icons too: what their armor and status effects make them weak or resistant to, shared so everyone sees the same. On by default (Resistances for player nameplates), with the same icon settings as other nameplates; Limited mode doesn't apply to them.

Status effect icons show only damage type effects: tar no longer shows with Icon style on Game icons.

## 0.8.4

A creature you hit or parry now counts as looked at for as long as the hit or parry reveals its bar (then the Hover delay runs as usual): full opacity, HP numbers and icons with the hover settings.

New Immunities setting for damage type icons: what a creature is immune to, as darker icons after its resistances. Notable only (default) leaves out spirit, chop and pickaxe, which most creatures are immune to; All shows every immunity (Limited mode recommended with it). Works with both icon styles.

Damage type icons are now sorted by strength. Left to right: immune, very resistant, resistant, slightly resistant, slightly weak, weak, very weak, so the strongest weakness sits at the bar's end. On boss bars the mildest sit next to the name, immunities outermost.

Creatures above two stars (from level mods) now show one star and the star count next to it, in the stars' yellow and the HP numbers' font, instead of no stars at all. Other mods that switch the nameplate's stars around can't override this; their star pictures are left as they are.

New Hit response for damage type icons (on by default): when a creature is hit by a type it's weak to, that icon flashes white, outline and all, and swells for a moment; types it resists or is immune to go darker and shrink instead. Other players' hits show too when the creature's owner has Enemy HUD.

Hit predictions now also update when one of your own effects runs out by itself (a mead, a forsaken power, rested), not only when it's removed.

The star count past two stars has its own setting (on by default); off is the game's own stars.

The Vanilla preset now also turns off everything added since it was made (star count, status effects, hit response, immunities, the dynamic and fade options, bubble bars, recovery overlay), and Author's Choice sets their defaults.

New Status effects (on by default): icons left of a creature's or other player's health bar while they're burning, frozen, shocked, poisoned or hit by spirit, using the same icons as those damage types (your shapes and colors). With Icon style on Game icons, the game's own status icons instead, tar included. Synced in multiplayer like the bubble bar.

Config reorganized into fewer sections, with clearer names and related settings next to each other (all your values carry over):

* 01 General: the preset.
* 02 Nameplates: Transparent background; then creatures (Creature HP numbers, Double size creature bars, Uppercase creature names, Star count past two stars, Critical hit flash, Status effects, Awareness display); then players (Player HP numbers, Double size player bars).
* 03 Boss Bar: HP numbers, HP numbers position, DPS, Name color.
* 04 Custom Bars: Stagger bar, Stagger recovery overlay, Bubble bar on nameplates, Bubble bar on your HUD.
* 05 Visibility: Reveal on hit or parry (was Show on damage and Show on parry), Keep nameplates on screen, then the hover settings: Creature HP numbers only on hover, Player HP numbers only on hover, Damage icons only on hover, Fade creature nameplates unless hovered, Fade player nameplates unless hovered. A creature you just hit or parried counts as hovered.
* 06 Damage Icons: Limited mode, Hit response, Creature icons and Boss icons (were four on/off settings, now Weaknesses and resistances / Weaknesses only / Resistances only / Off), Immunities, Icon style.
* 07 Hit Predictions, 08 Icon Shapes and Colors.
* 09 Fine Tuning: sizes (Text size, which was HP text size and Boss DPS text size; Icon size; Icons per row), positions (Star row height, Stagger bar gap, boss icon height and spacing), timing (Hover delay, which was three separate delays; Limited mode delay).
* 10 Multiplayer: Sync info, which now turns all sharing on or off: stagger, bubbles, status effects, critical hits and hit responses. Sync interval and Sync minimum change are gone.
* 11 Debug, last, only shown with Advanced settings ticked.
* Gone: Hide on unstaggerable enemies (unstaggerable creatures never get a stagger bar now).

New defaults (and Author's Choice): Limited mode and Damage icons only on hover are on, Player HP numbers only on hover is off.

Fixed the stagger bar flashing again on every hit (always with bows) against an already staggered enemy: the game tops the meter back up during a stagger, which looked like a new one.

Less network traffic: hit responses are only sent for players' hits, not creature fights, and the player nameplate crosshair check looks 100 m instead of 2 km.

## 0.8.3

Less work every frame for every nameplate:

* HP numbers are only rewritten when the number changes, instead of building new text and measuring it every frame.
* Bar sizes and the stagger bar's position are only set when they change.
* Keep bars on screen only measures a nameplate when it gets near the edge of the screen.
* Uppercase names are worked out once, not every frame.
* The crosshair check for player nameplate fading only runs while another player's nameplate is around (never in single player), without making new arrays each frame.
* The game's awareness icons are made invisible once instead of being switched off every frame after the game switches them on (which made the UI rebuild each time).
* Stagger sync no longer runs for every creature on every physics tick: nothing happens in single player, or for creatures that aren't staggered, and finished ones are forgotten instead of kept forever.
* The stagger bar's show/hide and the level stars' position are only set when they change.
* The flash length is read from the game once instead of on every flash.
* The boss DPS counter only measures its text when it changes.
* No work for hit and parry reveals while none are active.

New Dynamic mode for damage type icons (off by default): icons only show on the enemy you're looking at, with an optional delay (0 to 3 seconds) before they go after you look away. Bosses always show theirs. Separate from Limited mode, and both can be on.

New Dynamic HP numbers (off by default): the same for HP numbers on enemy bars. New Fade unless looked at (off by default): creature nameplates dim to 10% unless you're looking at them, like player nameplates do. Both share one Delay setting (0 to 3 seconds) for how long a creature still counts as looked at after you look away. Bosses always show fully.

Player nameplates get their own Dynamic HP numbers (on by default): HP numbers only show on the player your crosshair is on. A new Delay setting (0 to 3 seconds, default 0) keeps a player counted as aimed at for a moment after your crosshair leaves, for both their HP numbers and Fade unless aimed at.

Faded player nameplates no longer tint yellow: the damage trail under the health fill showed through it while see-through. While faded, the trail is now clipped to start where the fill ends, so it still shows without tinting the fill.

Limited mode delay now goes up to 3 seconds (was 2), like the new delays.

Hit predictions, Dynamic mode and Dynamic HP numbers all use the same check for which enemy you're looking at.

New Icon style setting in Damage Icons: Game icons uses the game's own skill and status effect icons instead of the shapes. Not recommended.

Config sections now show in number order in config editors.

## 0.8.2

Turned off now means fully off: the bubble bars, show on parry and keep bars on screen do no work at all while disabled. Your bubble and the crits on creatures you control are still shared, so other players with the mod keep seeing them.

Less work each frame for every nameplate: the mod no longer looks up the game's nameplate data by reflection every frame.

Show on damage no longer reads the health of every creature every frame. Only a creature you just hit is watched, until its health shows the hit landed (at most 4 seconds), so nothing runs while you aren't fighting.

## 0.8.1

Critical hit flashes are now sent to other players as a one-off message instead of a counter saved on every creature that ever took a crit. Old counters, and the leftover values of ended bubbles, are removed from the world's save as creatures load. Players on 0.8.0 and 0.8.1 won't see each other's crit flashes.

Less work each frame: nameplates no longer check a creature's network data for crits.

## 0.8.0

The stagger and critical hit flashes now work like the game's own: the bar's dark frame itself lights up white (darker inside, behind the bar's background), instead of a ring drawn around it. Like the game's, they hit at full brightness right away and then fade out. Neither flash shows on the hit that kills.

Bubble bar: while a player or creature is inside a protective bubble (Staff of Protection), a purple bar above its health bar shows how much the bubble can still take, with a gray trail as it gets hit; the name moves up to make room. On your own HUD it is a vertical bar right of your health bar (the forsaken power moves over) that fades in and out. When a bubble breaks, its bar flashes like a stagger bar, stays empty for a second, then disappears. The HUD bar has a notch at each quarter. Other players need Enemy HUD for their bubble to show. Both can be turned off in their own config section.

Removed the color settings for bars and text; the HUD uses the game's own colors. New Damage Icons section instead: every damage type has an icon (pick its shape from a dropdown) and a color (color picker, starts at the default), plus one color for all resistances. The config sections after it moved up a number; saved values carry over.

New visibility condition: parrying an enemy shows its health bar for 3 seconds, like hitting it does (Show on parry, on by default).

Player nameplates now dim to 10% (was 25%) until you aim at them, and switch instantly instead of fading.

Hit predictions (off by default, in their own config section): on the enemy you are looking at, the part of its health bar your next hit would take off and how far it would push its stagger bar show up. Uses your typical hit (or optionally the weakest possible one) with the game's own damage rules, including the creature's weaknesses and resistances, sneak attacks, the combo finisher bonus and double damage on staggered enemies. The stagger bar shows whenever there is a prediction for it. Shows the primary attack, and the secondary while you are doing it (can be swapped). Bare hands show nothing. Colors come from the game's own HUD, with a high contrast option.

## 0.7.4

Fixed level stars sometimes showing on normal creatures, when their health bar first appeared from being hit instead of looked at.

## 0.7.3

Added Limited mode (off by default): damage type icons only show for what you can deal right now, your weapon in hand plus its ammo. Optional delay before icons of a weapon you switched away from disappear (0 to 2 seconds, default 0).

Damage type icons now only update when something changes instead of every frame.

Resistance icons are darker but less transparent, so they stay readable while standing apart from weaknesses.

## 0.7.2

Physical damage icons (blunt, slash, pierce) are now white. Lightning is a lighter blue. Polished up some icons. Added icon size option.

## 0.7.1

Added chop icon.

## 0.7.0

Damage type icons, config overhaul and more.

