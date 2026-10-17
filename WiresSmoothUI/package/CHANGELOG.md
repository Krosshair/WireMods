# Changelog

## 0.5.4

* Fixed a visual issue with the health bar when fading

## 0.5.2

* Reworked config, completely
* Hotbar shows up when scrolling the hotbar on a gamepad
* Activator key toggle option

## 0.5.0

* Fixed the hotbar background flashing when the bar appears.
* The hotbar doesnt perma stay when you have something equipped.
* Combat-driven HUD visibility now depends only on enemies targeting or sensing the player.
* Lowered the threshold for health bar appearing to 95% instead of anything under 100%
* Food bar now appears briefly when a food first becomes replaceable instead of remaining visible for the whole replaceable period.
* Food bar appears during Valheim's final flashing food timer.
* Crosshair no longer becomes yellow on interactable hover
* Reworked config.

## 0.4.2

* FullHud map reveal no longer forces stamina, eitr, or adrenaline to appear; their normal independent visibility and fades are unchanged.
* Updated configuration descriptions. The other FullHud elements, MapOnly mode, map motion, hotkeys, and all unrelated behavior are unchanged.

## 0.4.1

* Added HUD Visibility -> Minimap reveal scope with FullHud (default) or MapOnly.
* Holding the existing minimap reveal keyboard/controller hotkey in FullHud mode also calls in health, food, Forsaken power, the vanilla hotbar and Azu quickslots, stamina/eitr/adrenaline, and My Little UI weight/slots.
* All added elements use their existing smooth 0.5-second fade, including normal visibility rules when the key is released. The minimap and its information keep the existing slide/quick effect timing.
* MapOnly restores the exact previous minimap/status/MLUI weather call-in behavior without revealing the other HUD panels.
* No changes to unrelated animations, hotkeys, or icon.

## 0.4.0

* Small minimap now stays visible for the entire time the player is actively controlling a ship.
* Ship HUD wind/direction now fades both in and out instead of snapping away when ship control ends.
* The fade-out keeps the ship HUD alive only until its CanvasGroup reaches zero, then disables it normally.

## 0.3.3

* Added smooth mouse-wheel scrolling for Valheim ScrollRect lists and panels.
* Rapid wheel input accumulates against the intended destination so scrolling stays responsive instead of feeling floaty.

## 0.3.2

Main update from public 0.3.12:

* Added a dynamic minimap call-in instead of leaving the small minimap permanently visible.
* Hold Backquote / tilde (or the configurable controller button) to call the minimap in from the right.
* Call-in is a 0.2 second sequence: the whole minimap slides in over 0.1 seconds with sine-out easing, then the exterior My Little UI day/time/weather/wind blocks and full status-effect panel fade in over 0.1 seconds.
* Releasing the call-in reverses the sequence: exterior text/effects fade out over 0.1 seconds, then the whole minimap retreats right over 0.1 seconds with sine-in easing.
* Everything inside the minimap moves together with no alpha fade, including the map, pins, markers, biome text and in-map wind direction.
* The map can be called in while mining, attacking or otherwise in combat; combat no longer suppresses the map or automatically forces the effect panel visible.
* New temporary status effects reveal themselves briefly, and expiring effects return for their final 5 seconds. These independent effect reveals keep the normal 0.5 second fade.
* Added a 0.5 second appearance fade to Valheim's ship wind indicator.
* HUD call-in/retreat is silent; no extra UI sound is played.

## 0.3.12

* Crosshair now only appears when using the Hammer or a ranged/projectile weapon instead of every held weapon/tool by default.
* The previous behavior can be restored with Crosshair \& Reticles -> Held item crosshair -> Everything.

