# Changelog

## 0.2.1

* Added Dynamic Camera: the view widens when you sprint or sail fast and when falling fast enough to hurt (a feather cape stays calm), narrows a little when you're out of stamina or overweight, dips when you land hard, and the camera trails and leads your movement slightly, up and down too. Camera rotation is untouched, so aiming stays exact. Config "5 - Dynamic Camera": Enabled, Strength, and a toggle per effect.
* Added Wide Lens (off by default): a wider field of view with barrel distortion, which magnifies the centre a little and keeps the edges where they were. Config "4 - Wide Lens": Enabled, Extra FOV (default +10), Barrel Distortion.

## 0.2.0

* Added a lens Depth of Field, used while the in-game Depth of Field setting is on. Blur works like a real lens: looking at something close softens what's behind it, looking into the distance blurs nothing.
* Focus comes from the area around the screen centre instead of a single point, so thin things crossing the centre don't make it jump. Your own character is ignored.
* Added near blur for things between the camera and your character, like grass right at the lens. Your character never blurs.
* Focus moves in about a fifth of a second, the same at any frame rate.
* Blur is a lens blur with round bokeh: blurred foreground spreads softly over what's behind it, background never bleeds forward.
* Quality setting: the new lens blur (default), or Valheim's blur with smoother focus.
* Added "3 - Depth of Field" config: Enabled, Quality, Blur In Time, Clear Time, Near Blur, Aperture.

## 0.1.0

* Renamed Wire's Shake it Up (Thunderstore/Hexium reupload and deprecate old, Nexusmods just renamed) to Wire's Camera Tweaks. Kept the full Wire's Shake It Up camera-shake rework and configuration.
* Kept smooth manual mouse-wheel/controller zoom.
* Kept smooth automatic zoom-out recovery after camera collision.

