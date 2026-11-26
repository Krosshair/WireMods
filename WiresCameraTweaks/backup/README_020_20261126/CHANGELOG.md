# Changelog

## Unreleased

* Added an optional lens Depth of Field (Quality: LensBlur), used while the in-game Depth of Field setting is on. Blur works like a real lens: looking at something close softens what's behind it, looking into the distance blurs nothing.
* Focus comes from the area around the screen centre instead of a single point, so thin things crossing the centre don't make it jump. Your own character is ignored.
* Added near blur for things between the camera and your character, like grass right at the lens. Your character never blurs.
* Focus moves in about a fifth of a second, the same at any frame rate.
* Background stays sharp while building, like when steering a ship.
* Blur is a lens blur with round bokeh: blurred foreground spreads softly over what's behind it, background never bleeds forward.
* Quality setting: Valheim's own blur with smoother focus (default), or the new lens blur.
* Added "3 - Depth of Field" config: Enabled, Quality, Blur In Time, Clear Time, Sharp While Building, Near Blur, Aperture.

## 0.1.0

* Renamed Wire's Shake it Up (Thunderstore/Hexium reupload and deprecate old, Nexusmods just renamed) to Wire's Camera Tweaks. Kept the full Wire's Shake It Up camera-shake rework and configuration.
* Kept smooth manual mouse-wheel/controller zoom.
* Kept smooth automatic zoom-out recovery after camera collision.

