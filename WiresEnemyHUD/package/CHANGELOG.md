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

