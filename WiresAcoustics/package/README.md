# Wire's Acoustics

Sound physics.

Inspired by the video [Physically-based audio: why most games sound fake [Voxel Devlog #31]](https://www.youtube.com/watch?v=oN4NZ0sDqKQ), on ray traced audio.

## Showcase video

Coming later, sorry lol. If you want, you can record your own, I guess.

## How it works

The mod traces rays through the world, the way light is traced in a renderer, but for sound. A few times a second, rays go out from your character's head in every direction and find out what's around you: how closed in you are, how big the space is, what it's made of, and where the openings are. Every sound you hear is then shaped by that space and by everything between it and you.

Which way a sound is comes from the camera, as you see it. How far it is, what's in the way and which room you're in come from your character's head, so zooming out doesn't make your own steps quieter and the camera slipping behind a wall doesn't muffle the world.

Wherever I could, the numbers come from real measurements and standards instead of being made up. The sources are at the bottom.

## Features

### Reverb from the real space
The reverb is worked out from what the rays hit: how big the space is, how closed in, and what it's made of. A stone hall rings, a log cabin sounds warmer, a cave sounds like a cave, and a furnished room is drier than an empty one. Windows, doors and gaps let sound out instead of counting the sky behind them as part of the room. Open ground is dry.

### Walls block sound like real walls
Walls, floors, roofs and doors block sound by how heavy they are, ranked by what real ones measure: a door leaks most, a plank wall less, stone least. The next room is muffled, not silent: a wall counts once, however many pieces it's built from, and each wall after the first takes a bit more. Sounds from outside don't ring through your room's reverb either: only what gets through to you does.

### Sound goes round things
A sound behind a wall also reaches you through the openings: a window, a door, a hole in the roof, or round a corner. Where you hear it from follows how hearing works: the sound through the wall arrives first and sets the direction, and the window takes over only when it's clearly louder (more than 10 dB, the precedence effect). So a deer right outside a window is heard at the window. Going round a corner costs what it really costs, more of the highs the longer the detour. Hills, rocks, trees and furniture in the way muffle by their size: a trunk barely matters, a boulder takes the highs off, a hill cuts a sound depending on how high it rises.

### Material matters
Every surface absorbs the lows and highs by published data for what it is. Snow swallows sound, moss and forest floors soften it, grass a bit less, paving and stone reflect it, water reflects almost everything. Just like IRL. Cool, right?

### Distance and air
Sounds fade with distance the way real sound does, instead of cutting out after a few metres: a quiet sound still reaches far, just quietly. Fires are as loud as how much is burning, so a sconce is a soft crackle and a hearth fills the room. The air takes the highs off far sounds as the international standard says it does, with the weather: warm, cold and freezing are matched to the real Norwegian weather Valheim is modelled on.

### Wind
Downwind a sound carries a few dB further; upwind it can drop a lot from about 100 m away, as measured outdoors. The wind itself is shaped by what's round you: in the lee of a wall, a rock or a hill it drops to a low rumble, it's heard from where it comes round and over what's in the way, and it gusts and lulls instead of blowing as one even loop.

### HRTF (headphones)
HRTF stands for head-related transfer function: how your head and ears change a sound depending on where it comes from. A sound on your left reaches your right ear a tiny bit later and duller, sounds behind you lose some brightness, and your outer ear colours sounds from above and below. The mod models this itself, for every sound, so you can tell front from back and up from down, not just left from right. Best with headphones. *I'd recommend turning HRTF off on speakers, as your real head already produces ACTUAL HRTF.* For the mod's own HRTF to sound its best, turn off Windows Sonic or Dolby Atmos spatial processing on your output device while playing Valheim; running both spatializers together can reduce directional clarity.

### Sounds come from where they're made
The game plays a lot of sounds at a character's feet or at the base of a piece. Here, weapon swings come from the weapon and follow the swing, a bow's draw from the bow, holstering from your back, eating from your head. Creatures roar from their head, claw with their arm and stomp with their feet. Chests sound from the lid, fires from the flame, crafting from the station. Anything that moves takes its sounds with it, and big sources (a hearth, a cart, a troll) sound as wide as they look.

### Weather and the sea
Indoors, the rain, wind and birds play from your doors and windows, just outside each one, so they're out there, not in the room with you. Through the walls round you only a quiet, dull rumble gets in. Close the door and that part goes. The sea is heard from the actual shore and fades like a long shoreline does; what's between you and it shades it like the wind, so a house blocks it but a post doesn't. Under water, the world above is about 30 dB quieter, as the physics of the water surface says.

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

The ray tracing runs on Unity's worker threads, and the per-sound processing runs on the audio thread: sounds too quiet to hear aren't processed. The mod asks Unity to mix 128 sounds at once instead of the game's 32, so in a busy base far sounds don't drop out and nothing is cut off mid-sound. The game's audio update does a small amount of bookkeeping on the main thread. Paths round walls are only traced for sounds that are blocked, and rooms that don't change are scanned less often, with a refresh when something is built, removed or a door opens.

## Settings

- **General:** Enabled, Room reverb, Muffling, HRTF (headphones), Realistic distance.
- **Tuning:** Reverb strength, Muffling strength, HRTF strength.
- **Debug:** Debug log: the measured space, muffled sounds and the mod's costs, every few seconds.
- Turning the mod off restores the game's own reverb zones, reverb sends and sound placement without a reload (for whatever is still loaded). Only the 128 voices stay until you restart the game: changing that restarts Unity's audio.

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
- **Outside sounds indoors:** through an opening, the share of the outside's all-round sound the opening shows you; checked against measured homes, outdoor to indoor about 10 dB with a window open ([Locher et al., 2018](https://doi.org/10.3390/ijerph15010149)).
- **Direction of sounds behind walls:** the precedence effect: the first arrival sets the direction unless a later one, 2 to 50 ms after it, is more than 10 dB louder ([Haas, 1951; Blauert, 1983](https://en.wikipedia.org/wiki/Precedence_effect)).
- **HRTF:** the delay between the ears from Woodworth's spherical head ([Woodworth, 1938](https://en.wikipedia.org/wiki/Interaural_time_difference)), head radius 8.75 cm ([Hartmann, How We Localize Sound](https://web.pa.msu.edu/acoustics/locsound.pdf), Physics Today, 1999). The rest of the HRTF (level, shading, the outer-ear notch) is modelled, not measured.
- **Under water:** the air and water impedances from [UNSW Physics](https://animations.physics.unsw.edu.au/jw/sound-impedance-intensity.htm), and the standard transmission at a boundary.
- **The sea as a line source:** [Hong Kong Environmental Protection Department](https://www.epd.gov.hk/epd/noise_education/web/ENG_EPD_HTML/m1/intro_6.html), noise basics.

Not from a source (judgement values): the wind in the lee of things (12 dB less, down to 800 Hz) and its gusts (up to 4 dB either way), how much a wall takes (the first one 12 to 22 dB by how heavy it is, each one after it 6 dB more and duller: measured walls made the next room silent), a wall counting once however many pieces it's built from, indoors 3 dB softer first reflections and 4 dB darker reverb (cozy), random calls kept off buildings within 10 m of you (moved out 4 m at a time), where you count as inside (a third to two thirds closed in), the reverb's loudness and tone, rooms heard at half their built size and as furnished homes (Valheim builds are about twice real size and bare), how much soft obstacles muffle, how much trees enclose a space, props' absorption, random bird and insect calls sitting 1 m above whatever is at their spot, upwind sounds held to 10 dB quieter at most (measured: 20 dB and more), the HRTF's amounts (level, shading, the outer ear's notch), past the game's own range for a sound, 6 dB more each time the distance doubles (instead of a cut-off), fires as loud as how much is burning (by flame width against a 1.5 m campfire, down to 18 dB quieter), where round a wall the mod looks for a way (just past the edge of what blocks it, 1 to 7 m to the sides and up to 5.5 m over), when something counts as in the way (0.3 of a wall), a player's own unplaced sounds made placed with a near distance of at least 1 m, and the wind in your ears (up to 2 dB louder facing it, 3 dB quieter with your back to it).

Disclaimer: development is AI assisted. Not fully human or AI.
Donations: ko-fi.com/krosshair
