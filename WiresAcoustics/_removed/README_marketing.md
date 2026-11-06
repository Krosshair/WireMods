# Wire's Acoustics

Sound shaped by the world around you, ray traced, for almost no performance cost.

Vanilla Valheim's reverb only depends on how far a sound is from the camera, and "indoors" is a single on/off switch: a tiny hut and a huge stone hall sound the same. This mod measures the actual space you're in, muffles sounds behind walls, and places every sound around your head.

## Video

**Coming later (very soon, gimme a day tbh).** A showcase video isn't recorded yet; the link will go here.

## What it does

* **Room reverb from the real space.** Rays fire from your ears in all directions a few times a second and measure how enclosed you are, how big the space is and what it's made of. Stone and marble ring, wood is warmer, soil and open sky swallow sound, and furniture, beds, rugs and banners break up the echo like real furnishings. A bare stone hall rings, a decorated room barely does, an open field sounds like the open field. Decay time comes from Sabine's reverberation formula, the reverb level from the room's absorption.
* **Distance you can hear.** Near sounds are mostly direct and far ones mostly room, like in a real room, so you can tell how far away something is.
* **Muffling through walls.** Sounds behind walls, floors, roofs, hills and rock get duller and quieter, more so through several layers. Furniture and trees only muffle a little (sound bends around them), and the thing a sound comes from never hides it.
* **Ambience.** Birds, wind, ocean and the biome loops go dull and quieter the more closed in you are, and keep the game's own left/right placement. An open door or window lets some through.
* **Under water.** Sounds that come from under the surface are muffled, as long as the whole source is under (a swimming greydwarf with its head up is not).
* **HRTF (headphones).** Every sound is placed around your head instead of plain left/right panning: the tiny delay and muffling of your far ear, duller sounds from behind, and the outer ear's cue for above and below. Turn it off on speakers.
* **Proximity Voice Chat (Azumatt).** Voices get the same HRTF and muffling as every other sound. Its own muffling is switched off while this mod is on, so nothing doubles up.

Everything is heard from the camera, never from your character: what you hear always matches what you see.

## Like Sound Physics Remastered, for Valheim

[Sound Physics Remastered](https://github.com/henkelmax/sound-physics-remastered) is the well-known Minecraft mod for "realistic sound attenuation, reverberation, and absorption through blocks". Wire's Acoustics is the same family of idea, built for Valheim and for cost:

||Sound Physics Remastered (Minecraft)|Wire's Acoustics (Valheim)|
|-|-|-|
|Reverb|Traces sound bouncing around the blocks|64 rays from your ears, one bounce, then the room acoustics formula (Sabine) from size and materials|
|Materials|Per block|Per building material (stone, marble, iron, wood ...), furniture and soft furnishings, soil, rock|
|Through walls|Absorption through blocks|Muffling by layers between you and the sound|
|Direction|From the paths sound takes|Our own HRTF on every sound|
|Voice chat|Simple Voice Chat|Proximity Voice Chat|

On top of that, Wire's Acoustics muffles positionless ambience indoors (keeping its stereo), muffles sounds from under water, and runs its rays and direction updates on Unity's worker threads.

## Performance

Measured with a main-thread profiler in a lived-in base (about 75 fps, \~13 ms per frame), five captures:

**0.025 to 0.036 ms per frame on the main thread.**

What that means in fps:

|Without the mod|With it|
|-|-|
|60 fps (16.7 ms)|59.9 fps|
|144 fps (6.9 ms)|143.3 fps|

The room rays and the direction updates run on Unity's worker threads; the per-sound processing (muffling, HRTF) runs on the audio thread, which doesn't hold up your frames. Wall checks for a sound happen once when it starts (and once a second for looping sounds, batched on worker threads).

## Settings

* **General:** Enabled, Room reverb, Muffling, HRTF (headphones).
* **Tuning:** Reverb strength, Muffling strength.
* **Debug (advanced):** Debug log: the measured space, what was muffled and the mod's own main-thread time, every few seconds.

Client side. Nothing to install on a server.

## Credits

The idea comes from Vercidium's devlog on ray traced audio ("Physically-based audio: why most games sound fake"). Comparison quote from the Sound Physics Remastered page.

Disclaimer: development is AI assisted. Not fully human or AI.
Donations: ko-fi.com/krosshair

