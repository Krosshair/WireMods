# Changelog

## 2.1.0

- Made automatic vertical melee targeting more reliable on slopes.
- Target selection now accounts for the actual width of an enemy's collider and the weapon's collision sweep, including its extra-height casts.
- Improved upper-body line-of-sight checks when the terrain hides an enemy's lower hitbox.
- Preserved the original weapon reach, horizontal attack arc, swing timing, and all recovery-cancel behavior.

## 2.0.0

- Added sprint recovery cancelling, alongside block, dodge, and jump, with configurable stamina costs.
- Added brief input buffering for jump and sprint to make defensive movement feel more responsive.
- Improved melee combo chaining with a little more time to continue an attack sequence.
- Smoothed post-hit movement and turning recovery, including gradual acceleration when sprint cancelling.
- Added short animation crossfades when transitioning out of cancelled attacks.

## 0.1.2

- Added jump as another recovery cancel alongside block and dodge.
- Jump cancel only works after the attack hits/fires and the normal recovery-cancel delay has passed.
- Uses Valheim's own jump input, movement and stamina, with a configurable extra cost (default 6).
- Does not jump out of wind-up, dodges, knockback, stagger or midair, and does not affect forced jumps.
- Keeps the existing block/dodge cancels and vertical melee aim unchanged.

## 0.1.1

- Added automatic invisible vertical melee hit aiming toward suitable enemies in front of the player.
- Ignores camera pitch when an eligible enemy is found; does not rotate the player or camera and does not provide horizontal tracking or target lock-on.
- Uses the weapon's original hit casts, range, width, attack arc and animations unchanged.
- Scans only when an actual melee hit is evaluated, using a reusable non-allocating 48-collider buffer and a single line-of-sight check on the selected target.
- Limits correction to 65 degrees and selection to a configurable narrow cone within the normal horizontal swing.
- Keeps 0.1.0 defensive recovery cancels unchanged.

## 0.1.0

- Added limited attack-recovery cancelling into dodge or block.
- Cancels only become available after the attack's real hit/projectile trigger, so wind-up commitment is preserved.
- Dodge cancels cost the normal dodge stamina plus an extra configurable surcharge.
- Block cancels use a smaller configurable stamina surcharge.
- Secondary/special attacks add an extra configurable cancel surcharge.
- Queued dodge takes priority over block, and a defensive cancel clears previously buffered attack presses.
- Uses Valheim's existing attack/dodge queues instead of replacing its input system.
- Client-side only; no custom network protocol or combat-stat changes.
