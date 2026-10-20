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

