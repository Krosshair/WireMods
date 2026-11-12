# Wire's Acoustics

Sound physics.

Inspired by the video [Physically-based audio: why most games sound fake [Voxel Devlog #31]](https://www.youtube.com/watch?v=oN4NZ0sDqKQ), on ray traced audio.

## Showcase video

Coming later, sorry lol. If you want, you can record your own, I guess.

## How it works

The mod traces rays through the world, the way light is traced in a renderer, but for sound. A few times a second, rays go out from the camera in every direction and find out what's around you: how closed in you are, how big the space is, what it's made of, and where the openings are. Every sound you hear is then shaped by that space and by everything between it and you.

You hear from the camera. Where the camera is, that's where you are.

Wherever I could, the numbers come from real measurements and standards instead of being made up. The sources are at the bottom.

## Features

### Reverb from the real space
The reverb is worked out from what the rays hit: how big the space is, how closed in, and what it's made of. A stone hall rings, a log cabin sounds warmer, a cave sounds like a cave, and a furnished room is drier than an empty one. Windows, doors and gaps let sound out instead of counting the sky behind them as part of the room. Open ground is dry.

### Walls block sound like real walls
Walls, floors, roofs and doors block sound as much as real ones measure, by material and thickness. A wood wall is like a solid timber wall, a door leaks more, stone is like a brick wall. Through a wall you hear a muffled but still recognisable sound, not just a rumble.

### Sound goes round things
A sound behind a wall also reaches you through the openings: a window, a door, a hole in the roof, or round a corner. Where you hear it from follows how hearing works: the sound through the wall arrives first and sets the direction, and the window takes over only when it's clearly louder (more than 10 dB, the precedence effect). So a deer right outside a window is heard at the window. Going round a corner costs what it really costs, more of the highs the longer the detour. Hills, rocks, trees and furniture in the way muffle by their size: a trunk barely matters, a boulder takes the highs off, a hill cuts a sound depending on how high it rises.

### Material matters
Every surface absorbs the lows and highs by published data for what it is. Snow swallows sound, moss and forest floors soften it, grass a bit less, paving and stone reflect it, water reflects almost everything. Just like IRL. Cool, right?

### Distance and air
Sounds fade with distance the way real sound does, instead of cutting out after a few metres. The air takes the highs off far sounds as the international standard says it does, with the weather: warm, cold and freezing are matched to the real Norwegian weather Valheim is modelled on.

### Wind
Downwind a sound carries a few dB further; upwind it can drop a lot from about 100 m away, as measured outdoors. You also hear the wind in your ears from the way it's blowing.

### Sound takes time
Sound travels at the speed of sound. A tree falling far away is heard after you see it.

### Doppler
Moving sounds rise in pitch as they come toward you and drop as they go away. Ships' wakes too.

### HRTF (headphones)
HRTF stands for head-related transfer function: how your head and ears change a sound depending on where it comes from. A sound on your left reaches your right ear a tiny bit later and duller, sounds behind you lose some brightness, and your outer ear colours sounds from above and below. The mod models this itself, for every sound, so you can tell front from back and up from down, not just left from right. Best with headphones. *I'd recommend turning HRTF off on speakers, as your real head already produces ACTUAL HRTF.* For the mod's own HRTF to sound its best, turn off Windows Sonic or Dolby Atmos spatial processing on your output device while playing Valheim; running both spatializers together can reduce directional clarity.

### Sounds come from where they're made
The game plays a lot of sounds at a character's feet or at the base of a piece. Here, weapon swings come from the weapon and follow the swing, a bow's draw from the bow, holstering from your back, eating from your head. Creatures roar from their head, claw with their arm and stomp with their feet. Chests sound from the lid, fires from the flame, crafting from the station. Anything that moves takes its sounds with it, and big sources (a hearth, a cart, a troll) sound as wide as they look.

### Weather and the sea
Indoors, rain, wind and birds come in through the windows and doors from their direction; through closed walls only a faint rumble gets in. The sea is heard from the actual shore and fades like a long shoreline does. Under water, the world above is about 30 dB quieter, as the physics of the water surface says.

### Portals
Going through a portal, its hum doesn't stop and restart: it carries through with you and keeps playing from the exit portal. More of an extra than sound physics, but it's audio-related so it's here.

### Also
- No pitch drop in the Mistlands mist (fog doesn't do that to sound, and I don't like it).
- The main menu's sounds get the HRTF too. Barely noticeable there, tbh, but it's there.

## Works with

- **Proximity Voice Chat (Azumatt):** voices come from the speaker's head and get the walls, openings and HRTF like any sound.
- **Wire's Stackify:** its chest sounds come from each chest.
- (kinda) **Immersive Weather & Acoustics:** set its `EnableAudioOcclusion = false` so only one audio system runs; its visual weather can stay on.

## Performance

The ray tracing runs on Unity's worker threads, and the per-sound processing runs on the audio thread. The game's audio update does a small amount of bookkeeping on the main thread. Paths round walls are only traced for sounds that are blocked, and rooms that don't change are scanned less often, with a refresh when something is built, removed or a door opens.

## Settings

- **General:** Enabled, Room reverb, Muffling, HRTF (headphones), Realistic distance.
- **Tuning:** Reverb strength, Muffling strength, HRTF strength.
- **Debug:** Debug log: the measured space, muffled sounds and the mod's costs, every few seconds.
- Turning the mod off restores the game's own reverb zones, listener position and sound placement without a reload (for whatever is still loaded).

## Sources

Every value below was checked against the source it's credited to.

- **Air absorption:** [ISO 9613-1:1993](https://www.iso.org/standard/17426.html), Attenuation of sound during propagation outdoors, part 1 (Bass, Sutherland, Zuckerwar, Blackstock and Hester's formula). Weather: [Bergen (Florida)](https://en.wikipedia.org/wiki/Bergen) July and [Finsevatn](https://en.wikipedia.org/wiki/Finse) January climate normals, 1991-2020.
- **Barriers, corners, hills, obstacles:** Maekawa's barrier formula as Kurze and Anderson wrote it ([Sound attenuation by barriers](https://doi.org/10.1016/0003-682X(71)90024-7), Applied Acoustics 4, 1971); capped at 20 dB as in [ISO 9613-2:1996](https://www.iso.org/standard/20649.html) for a single edge (both as summarised by [Arup](https://strutt.arup.com/help/Environmental_Noise/EnviroBarrierAtten.htm)).
- **Walls and doors:** [WoodWorks, Mass Timber Acoustics](https://www.woodworks.org/wp-content/uploads/wood_solution_paper-MASS-TIMBER-ACOUSTICS.pdf) (bare cross-laminated timber walls, STC 33 at 78 mm, 38 at 175 mm); [Commercial Acoustics](https://commercial-acoustics.com/sound-advice/stc-rating-chart/) (solid-core wood door, STC 30 to 35); [Engineering ToolBox](https://engineeringtoolbox.com/sound-transmission-massive-walls-d_1409.html) (230 mm brick plastered both sides, STC 55, and the average shape of a wall's loss over frequency). Iron and ice: the mass law, as in [Warnock, Factors Affecting Sound Transmission Loss](https://web.mit.edu/parmstr/Public/NRCan/CanBldgDigests/cbd239_e.html) (NRC Canadian Building Digest 239, 1985).
- **Surface absorption:** [Sengpiel's table](https://sengpielaudio.com/calculator-RT60Coeff.htm) (brick, marble, wood on joists, water or ice, concrete); the [pyroomacoustics materials database](https://pyroomacoustics.readthedocs.io/en/stable/pyroomacoustics.materials.database.html) (furniture, rugs, curtains, limestone); [heyizhou.net's absorption tables](https://heyizhou.net/notes/absorption-coefficients) (steel decking; heavy wood from Vorländer's Auralization).
- **Ground:** the Nord2000 ground classes (flow resistivity), as listed in the [d&b NoizCalc technical white paper](https://www.dbaudio.com/assets/products/downloads/software/noizcalc/dbaudio-noizcalc-technical-white-paper-1.6-en.pdf); turned into absorption with Delany and Bazley's model ([Acoustical properties of fibrous absorbent materials](https://doi.org/10.1016/0003-682X(70)90031-9), Applied Acoustics 3, 1970).
- **Trees:** Li, Van Renterghem, Kang, Verheyen and Botteldooren, [Sound absorption by tree bark](https://doi.org/10.1016/j.apacoust.2020.107328), Applied Acoustics 165 (2020).
- **Room size and reverb:** Sabine's reverberation formula, RT60 = 0.161 V / A ([Sengpiel](https://sengpielaudio.com/calculator-RT60.htm)); the critical distance, 0.057 √(QV/RT) ([Arup](https://strutt.arup.com/help/Electroacoustics/DCritical.htm)).
- **Wind:** [Waddington and Lam, The effect of weather conditions on the propagation of sound](https://www.ioa.org.uk/system/files/proceedings/dc_waddington_yw_lam_the_effect_of_weather_conditions_on_the_propagation_of_sound.pdf) (Proceedings of the IOA 24, 2002); the US National Weather Service's [blizzard definition](https://forecast.weather.gov/glossary.php?word=bliz) to match the game's wind to real wind speeds.
- **Outside sounds indoors:** room-to-room transmission, L2 = L1 - R + 10 lg(S/A) ([Arup](https://strutt.arup.com/help/Building_Acoustics/Room2Room.htm)); checked against measured homes, outdoor to indoor 10 dB with windows open and 27.8 dB closed ([Locher et al., 2018](https://doi.org/10.3390/ijerph15010149)).
- **Direction of sounds behind walls:** the precedence effect: the first arrival sets the direction unless a later one, 2 to 50 ms after it, is more than 10 dB louder ([Haas, 1951; Blauert, 1983](https://en.wikipedia.org/wiki/Precedence_effect)).
- **HRTF:** the delay between the ears from Woodworth's spherical head ([Woodworth, 1938](https://en.wikipedia.org/wiki/Interaural_time_difference)), head radius 8.75 cm ([Hartmann, How We Localize Sound](https://web.pa.msu.edu/acoustics/locsound.pdf), Physics Today, 1999). The rest of the HRTF (level, shading, the outer-ear notch) is modelled, not measured.
- **Under water:** the air and water impedances from [UNSW Physics](https://animations.physics.unsw.edu.au/jw/sound-impedance-intensity.htm), and the standard transmission at a boundary.
- **The sea as a line source:** [Hong Kong Environmental Protection Department](https://www.epd.gov.hk/epd/noise_education/web/ENG_EPD_HTML/m1/intro_6.html), noise basics.

Not from a source (judgement values): the reverb's loudness and tone, rooms heard at half their built size and as furnished homes (Valheim builds are about twice real size and bare), how much soft obstacles muffle, how much trees enclose a space, props' absorption, random bird and insect calls sitting 1 m above whatever is at their spot, upwind sounds held to 10 dB quieter at most (measured: 20 dB and more), the HRTF's amounts (level, shading, the outer ear's notch), a quiet sound fading out between 1.5 and 3 times the game's own range for it, sounds behind walls getting more of the room's reverb, where round a wall the mod looks for a way (just past the edge of what blocks it, 1 to 7 m to the sides and up to 5.5 m over), when something counts as in the way (0.3 of a wall), how strongly the outside leans toward the openings, a player's own unplaced sounds made placed with a 5 m near distance, and the wind in your ears (up to 2 dB louder facing it, 3 dB quieter with your back to it).

Disclaimer: development is AI assisted. Not fully human or AI.
Donations: ko-fi.com/krosshair
