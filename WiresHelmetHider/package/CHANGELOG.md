# Changelog

## 0.2.5

* The toggle animation now also plays while sitting in a chair or throne.

## 0.2.4

* Fixed a console spam, oopsies

## 0.2.3

* Helmet-hidden state now persists per character across world reloads, game restarts and the character-selection preview. Hidden characters are stored by Valheim's stable numeric player ID in a separate file under BepInEx/config: wire.valheim.helmethider.hidden.txt.
* File format uses one bare player ID per line: 123456789. Presence means hidden. Showing a helmet or equipping, unequipping or swapping a headpiece (when Reset on headgear change is enabled) removes the corresponding entry.
* The in-memory state updates immediately, but the file is only rewritten after a successful character save (including normal logout)

## 0.2.2

* Headpiece toggles briefly play the shared Valheim equip pose ("equipping"), without queuing an actual equipment action or slowing movement. The actual helmet visibility change is delayed until the cosmetic animation ends; interruption by attacking or starting a real equip action applies it immediately. Short animation duration (0.08–0.40 seconds, default 0.22 seconds) before actually toggling the hide state, animation can be cancelled and does not impair any controls or movement as it's purely visual.
* Hidden state resets on actual headgear change, swap or removal by default. Disable the reset option to keep helmet hidden across equipment changes.

