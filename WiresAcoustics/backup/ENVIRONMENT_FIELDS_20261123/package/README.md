# Wire's Acoustics

**0.7.0 release candidate — revised rain geometry/response and source shape require in-game confirmation before publishing.**

**Occlusion revision:** Building and door edits trigger prioritized acoustic rechecks for nearby playing sounds. Obstruction is determined from current geometry, with diffraction computed from real detected edge paths instead of a hard `0.3` blocked-state threshold. Known wall edges now also produce continuous clear-side diffraction. Edge-transition and performance comparisons remain useful final regression checks before publication.

0.7.0 also lets independent positional machines and fires with the same loop play at the same time, with separately randomized playback positions. No experimental loop start/stop fade remains. It corrects rotated sound-source geometry, makes Doppler smoothing independent of frame rate, stabilizes direct HRTF direction against the current camera pose, processes up to four measured opening routes, smoothly blends precedence-effect transitions, and evaluates ocean sheltering across observed shoreline directions. It retains the original Valheim ocean recording instead of duplicating it as artificial wave voices. The deliberate effective room-scale factor of 0.5 is retained. These are source-verified changes, not a claim of completed listening tests.

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

### Material transmission and diffraction
Valheim's structural materials are mapped to published Steam Audio transmission-energy presets rather than using an arbitrary first-wall/next-wall volume penalty. For a measured single box-collider wall, WA combines continuous projected source coverage with an approximate Fresnel knife-edge diffraction field: barely crossing an edge stays substantially audible; moving deeper into the shadow progressively changes loudness, filtering and apparent arrival direction. Other geometry, multiple walls and complex routes still use the existing bounded ray and portal solver, so full-scene continuity remains subject to in-game validation. Reverb sends follow the received signal rather than treating blocked sources as fully exposed.

### Sound goes round things
A sound behind a wall also reaches you through the openings: a window, a door, a hole in the roof, or round a corner. Where you hear it from follows how hearing works: the sound through the wall arrives first and sets the direction, and the window takes over only when it's clearly louder (more than 10 dB, the precedence effect). So a deer right outside a window is heard at the window. Going round a corner costs what it really costs, more of the highs the longer the detour. Hills, rocks, trees and furniture in the way muffle by their size: a trunk barely matters, a boulder takes the highs off, a hill cuts a sound depending on how high it rises.

### Material matters
Every surface absorbs the lows and highs by published data for what it is. Snow swallows sound, moss and forest floors soften it, grass a bit less, paving and stone reflect it, water reflects almost everything. Just like IRL. Cool, right?

### Distance and air
Sounds fade with distance the way real sound does, instead of cutting out after a few metres: a quiet sound still reaches far, just quietly. Sound takes time to travel, so far sounds arrive late, and sounds coming toward you or going away shift in pitch (Doppler), both from your character's head. Continuous fire sounds use each Valheim AudioSource's original sound range to set their relative level; collider size does not determine their volume. The air takes the highs off far sounds as the international standard says it does, with the weather: warm, cold and freezing are matched to the real Norwegian weather Valheim is modelled on.

### Wind
Downwind positioned world sounds can become louder, while upwind they grow quieter over distance. This affects machines, fire, animals, combat and other located sources. The game samples a simplified, measured outdoor model from the FHWA's traffic-noise results (nearby hard/soft ground, head height, sound distance and upwind/downwind speed) instead of fixed +3/−10 dB limits; the effect is only approximate for Valheim's varied sounds. The effect follows the same already-measured wind exposure as the ambient wind: sheltered behind a building, rock or hill it weakens smoothly, reaching zero wind influence in complete shelter. The acoustic model uses the listener's local shelter without additional wind raycasts at every sound source. The wind itself is shaped by what's round you: in the lee of a wall, a rock or a hill it drops to a low rumble, it's heard from where it comes round and over what's in the way, and it gusts and lulls instead of blowing as one even loop. Biome recordings that actually contain wind, such as the wind-in-reeds ambience used by clear-weather Plains, also take a broad windward direction and measured shelter response; rain and wildlife recordings remain diffuse.

### HRTF (headphones)
HRTF stands for head-related transfer function: how your head and ears change a sound depending on where it comes from. A sound on your left reaches your right ear a tiny bit later and duller, sounds behind you lose some brightness, and your outer ear colours sounds from above and below. The mod models this itself, for every sound, so you can tell front from back and up from down, not just left from right. Best with headphones. *I'd recommend turning HRTF off on speakers, as your real head already produces ACTUAL HRTF.* For the mod's own HRTF to sound its best, turn off Windows Sonic or Dolby Atmos spatial processing on your output device while playing Valheim; running both spatializers together can reduce directional clarity.

### Sounds come from where they're made
The game plays a lot of sounds at a character's feet or at the base of a piece. Here, weapon swings come from the actual equipped blade/head mesh as it moves, with occlusion sampled across that weapon rather than the grip point; a bow's draw from the bow, holstering from your back, eating from your head. Creatures roar from their head, claw with their arm and stomp with their feet. Chests sound from the lid, fires from the flame, crafting from the station. Anything that moves takes its sounds with it. Sounds from genuinely distributed emitters sample their owner's cached collider bounds, whether they loop or play once. Localized actions like a hinge, lid or hand movement are point emitters even when their owner is large. Unknown sources are points by default, not invented 30 cm spheres.

### Weather and the sea
Indoors, wind and biome ambience still come through the most audible doors and windows. Rain uses only Valheim's original ambient loop: WA continuously filters its gain and frequency spectrum according to measured room enclosure, roof and distance to cached openings. Near a window or large open arch, the same recording becomes clearer and stronger; farther inside it becomes quieter and duller without disappearing. WA combines open-sky/horizon visibility, cached nearby doorway/window geometry, and direct visibility from the character's listening position to exterior air above actual nearby terrain. The distance to that outdoor ground determines the rain's direct level; the sight check covers all horizontal directions rather than depending on camera facing or whether a sparse room ray happened to miss an opening. Rainy room surveys include a limited additional asynchronous set of terrain-view and overhead shelter rays. The overall enclosure fraction does not suppress that direct opening path. The distinct sound of rain striking the roof is emphasized only when the measured overhead surface is a real roof piece, with its strength decreasing as that roof gets farther away. Floors between storeys do not count as the outside roof, so basements retain the quieter, filtered rain transmission instead of sounding like attics. No separate rain patches, rain voices or weather bridge are played. Valheim's own audio transition determines when a different weather recording begins. Through the walls round you only a quiet, dull rumble gets in. Close the door and that part goes. The sea is heard from the actual shore and fades like a long shoreline does; what's between you and it shades it like the wind, so a house blocks it but a post doesn't. Under water, the world above is about 30 dB quieter, as the physics of the water surface says.

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

- **General:** Enabled, Room reverb, Muffling, HRTF (headphones), Realistic distance, Travel time, Doppler, Under water, Air, Wind, Sound placement, Voice chat.
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

Not from a source (judgement values): the wind in the lee of things (12 dB less, down to 800 Hz) and its gusts (up to 4 dB either way), the approximations used to blend partial enclosure and multiple obstruction paths, a wall counting once however many pieces it's built from, indoors 3 dB softer first reflections and 4 dB darker reverb (cozy), random calls kept off buildings within 10 m of you (moved out 4 m at a time), where you count as inside (a third to two thirds closed in), the reverb's loudness and tone, rooms heard at half their built size and as furnished homes (Valheim builds are about twice real size and bare), how much soft obstacles muffle, how much trees enclose a space, props' absorption, random bird and insect calls sitting 1 m above whatever is at their spot, upwind sounds held to 10 dB quieter at most (measured: 20 dB and more), the HRTF's amounts (level, shading, the outer ear's notch), past the game's own range for a sound, 6 dB more each time the distance doubles (instead of a cut-off), fire strength normalized to each fire loop's original authored audio range (10 m reference), where round a wall the mod looks for a way (just past the edge of what blocks it, 1 to 7 m to the sides and up to 5.5 m over), how finite sampled ray paths approximate a partially covered emitter, a player's own unplaced sounds made placed with a near distance of at least 1 m, the wind in your ears (up to 2 dB louder facing it, 3 dB quieter with your back to it), the two rays beside every line 8 cm off (two of three must agree), a line through more than 16 things counting as through a plank wall at least, and a door's sounds 15 cm in from its hinge.

Disclaimer: development is AI assisted. Not fully human or AI.
Donations: ko-fi.com/krosshair
