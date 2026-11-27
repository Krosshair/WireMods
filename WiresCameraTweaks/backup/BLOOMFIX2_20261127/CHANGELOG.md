# Changelog

## 0.3.0

* Replaced the blur of Valheim's bloom with convolution bloom: lights glow in the shape of real glare in the eye (Spencer et al. 1995), a bright core with a soft halo reaching across the screen, computed through an FFT on the graphics card. Same threshold and overall amount of glow as Valheim's. Needs the in-game Bloom setting on.
* Lens dirt and HDR tonemappers like RenoDX keep working with it.
* Added "4 - Bloom" config: Enabled, Intensity.

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

