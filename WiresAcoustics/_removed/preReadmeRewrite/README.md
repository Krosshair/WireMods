# Wire's Acoustics

Ray traced acoustics for Valheim: the space around you shapes every sound. Client side.

Everything is heard from the camera, never from the character.

## Video

Coming later (not recorded yet).

## Features

- **Room reverb, measured.** 64 rays from the camera measure the space five times a second: how enclosed it is, how big, and what it's made of. Sabine's room formula turns that into the reverb, so a stone hall rings, a log cabin is warmer, and a cave sounds like a cave. Outdoors is measured the same way: open sky lets the sound out, so open ground is dry.
- **Forests have their own sound.** Trunks all round scatter sound into a soft tail (up to about 1.8 s in a dense forest, its highs taken by the leaves and needles), but they aren't walls: a dense swamp doesn't sound like a cave, trunks muffle little and never echo.
- **Clutter counts.** Furniture breaks up and swallows reflections, and soft things (beds, rugs, banners) swallow the most. A decorated room sounds lived in; an empty one echoes.
- **The ground matters.** Each ray's ground is judged where it lands: snow swallows sound, grass and the forest floor soften it, rock, stone floors and paving reflect it, and water reflects almost everything. What you've made of the ground counts too: paved, tilled or dug.
- **Muffling through walls.** One ray from you to each sound counts what's in between: walls, floors, hills and rocks count fully, furniture partly, tree trunks a little, and the thing making the sound never counts. More layers make the sound quieter and duller.
- **Sound finds its way round.** A blocked sound also tries paths through the openings you're near (windows, doors, holes in the roof), round the corners of what's in the way, and by bouncing off up to two surfaces. The direct sound through the wall and the loudest way round are both heard, added up the way sound energy adds: a deer outside is heard muffled through the wall and clearer from the window, leaning toward whichever is louder.
- **Echo.** Out in the open, a cliff, hillside or wall facing you sends sounds back from its side. Each sound gets its own echo: as late as its way there and back is longer than its way to you, and as much quieter. A bare rock face echoes clearly, a snow slope barely. Steady loops don't echo (you only hear a sound's starts and stops come back).
- **HRTF (headphones).** Our own model of how your head and ears hear direction: a tiny delay and dulling for the far ear, duller sounds behind you, and the outer ear's cue for above and below. You can tell front from back and up from down, not just left from right. Wide sources stay wide.
- **Rain and wind indoors.** Heard both ways at once: muffled through the walls from all around, and clearer from the openings' direction, as much as the openings make up of what you hear. Rain indoors leans toward the window.
- **The sea, from the sea.** The ocean loop is placed where the waves break on the nearest real shore, so it's on your left when the water is, and walls and openings shape it like any sound (the beach itself doesn't hide it).
- **Under water.** With the camera under the surface everything is muffled. Sounds whose source is fully under water are muffled for you above it.
- **Distance.** Sounds get quieter with distance the way real sound does, 6 dB each time the distance doubles, instead of the game's own curves (many reach silence within a few metres: a hearth is silent 10 m away). Quiet sounds fade out sooner than loud ones: a sconce's crackle is gone long before a hearth's roar. The air dulls a sound's highs the further it travels, so a fight 60 m away is duller, not just quieter. Wind carries sound: downwind of a far sound you hear it better, upwind worse. The game's random ambience (birds, gusts, insects round you) keeps its own fade, since it stands for the world around you, not one creature.
- **Sound takes time.** Sound travels at 343 m/s: a tree falling 170 m away is heard half a second later, and a sound out there stops that much later too. Moving sounds shift in pitch as they come and go (Doppler), our own, so the camera bumping about doesn't make sounds warble.
- **Sounds that move with their source.** A creature's roar, a hit or a death sound follows the creature instead of staying where it started; a death wail follows the falling body. A creature's calls come out of its front: duller when it faces away.
- **Your own sounds, from you.** Every sound of your own moving and doing (steps, jumps, equipping, drawing a bow, reloading, casting) comes from your character and moves with it, including the ones the game plays "in the camera". Skill level-ups and the interface stay as they are.
- **Sounds have a shape.** A sound takes the rough shape of what makes it (a hearth, a door, a cart, a creature's body): it's hidden only as much as its least hidden part, so anything poking out past a wall or another piece is heard, and it sounds as wide as it looks from where you are (a point across the hall, all in front of you up close).
- **Mist.** The game drops the pitch of distant sounds inside the Mistlands mist. Fog doesn't do that to sound, so here the mist sounds like anywhere else.
- **Main menu.** The menu's own sounds get the HRTF too: a first taste before you load in.
- **Portals carry their hum.** Going through, the portal's hum isn't stopped and started again: it glides to where the exit portal will be around you, rides with you while you load, and carries on as the exit portal's own hum.
- **Replaces the game's reverb.** The game's reverb zones (its one distance-only reverb everywhere, and those of dungeons) and its fixed "Indoor" mix under any roof are replaced by the measured space (the Indoor mix is still used inside a shield dome).

### Works with

- **Proximity Voice Chat (Azumatt):** voices get the HRTF, walls and openings like any sound. Its own muffling is held open so nothing applies twice.
- **Wire's Stackify:** its chest sounds after a quick stack or restock come from each chest, as world sounds.

## Performance

| Work | Where it runs |
|---|---|
| Room rays and their bounces, sea rays, paths round walls, rechecks of playing sounds and voices, directions and distances | Unity worker threads (jobs) |
| Muffling, air, echo and HRTF per sample | Audio thread |
| One wall ray per new sound, finding openings, sounds following their owner, applying results | Main thread |

Measured with a main-thread profiler in a lived-in base: **about 0.1 ms per frame** on the main thread (0.098 ms in the latest capture, 0.5% of a 19 ms frame), slowest single frame under 1 ms. A sound starting costs a few microseconds.

| Without the mod | With it |
|---|---|
| 60 fps (16.7 ms) | 59.6 fps |
| 144 fps (6.9 ms) | 142 fps |

Audio-thread work has levels of detail, like a game's model LODs. Every playing sound is processed; the nearest (up to 32, fewer if the audio thread gets busy: it's measured) get the full HRTF, the rest a lighter direction that you can't tell apart at that distance anyway, so a crowded swamp in the rain can't overload it. Switching between the two is crossfaded. Filters are never switched on and off while sounds play (that made Unity wait for its audio mixer, a few ms at a time).

## The numbers

- **Room:** 64 directions, 50 m. Enclosure is the share of level and upward rays that hit something. RT60 = 0.04 x mean free path / absorption. Reverb level follows the room constant (1 - a) / a. Absorption: stone 0.03, marble 0.02, iron 0.04, hardwood 0.09, wood 0.12, furniture 0.3, soft furniture 0.55, rock 0.04, trees 0.8, water 0.01; the ground by biome: meadow grass 0.4, forest floor 0.55, swamp mud 0.15, snow 0.7, dry grass 0.35, Mistlands rock and moss 0.3, Ashlands 0.25, sand 0.3; paved 0.03, tilled 0.55, dug 0.25; open sky absorbs fully. Typical measured mid-frequency values.
- **Reverb send per sound:** d / (d + r), with r = 2 m + 10 m x absorption (near sounds mostly direct, far ones mostly room), everywhere.
- **Forest:** by the share of level rays hitting a trunk: a tail of 0.5 to 1.8 s, highs damped, some 10 to 20 dB under the direct sound.
- **Walls:** 0 = 100%, 1 = 60% and 1500 Hz, 2 = 40% and 800 Hz, 3+ = 28% and 500 Hz, interpolated. Changes glide over about 0.15 s; a sound starting takes its value at once. Looping sounds are rechecked once a second, long one-off sounds twice a second.
- **Paths:** openings are clusters of escaped room rays (up to 4 kept, the 2 biggest tried). Corners: 2.5 m and 5 m to each side of the first blocker and 3 m above. Bounces: each room ray reflects once more (angle in = angle out), the 3 shortest bounce paths tried. Cost: walls on the way + bend (0.3 walls at 90 degrees) or reflection (absorption, and how far off the mirror direction), volume by direct / path length.
- **Echo:** surfaces 15 to 50 m away hit nearly head-on by at least two rays, as strong as they're hard (1 - absorption). Per sound: delay = (sound to wall + wall to you - sound to you) / 343 m/s, level = (sound to you) / (that path), at most half. Low-passed at 4 kHz (duller from behind), fades out as you go indoors, scaled by Reverb strength; one-off sounds within 50 m only.
- **HRTF:** Woodworth's interaural delay (head radius 8.75 cm, up to 0.66 ms), far ear up to 6 dB quieter and low-passed down to 1.2 kHz, near ear up to 1.6 dB louder, highs shaded behind you above 2.5 kHz, a narrow outer-ear notch from 5 kHz (below) to 10 kHz (above) only for sounds well above or below ear level. Scaled down for wide sources; HRTF strength scales it all.
- **Ambience:** volume down to 30% and a 700 Hz low-pass fully enclosed, opened up by nearby openings; leans up to 80% toward them.
- **Sea:** 96 downward rays once a second (16 directions, 10 to 110 m) find where the water starts; the loop moves to the nearest shoreline, a metre above the surface.
- **Under water:** 45% and 400 Hz.
- **Air:** low-pass about 15 kHz at 30 m, 9 kHz at 60 m, 4.5 kHz at 100 m, never below 2.5 kHz.
- **Distance:** logarithmic from the game's own near distance to 200 times it; audible out to between 1.5 and 3 times the game's range, faded over that (the game gives quiet sounds a short range). Wind: up to 30% louder downwind and 35% quieter upwind, from 15 m on.
- **Travel:** start and stop delayed by distance / 343 m/s. Doppler: pitch x 343 / (343 + speed away), the speed smoothed and capped at 50 m/s, ignored when the camera jumps.

## Compared to other mods

### Sound Physics Remastered (Minecraft)

[Sound Physics Remastered](https://github.com/henkelmax/sound-physics-remastered) provides "realistic sound attenuation, reverberation, and absorption through blocks" for Minecraft by tracing sound bouncing through the world. Same family of idea. Here: two bounces per ray (for paths) plus the room acoustics formula (for the reverb) instead of many bounces, per-material and per-furniture absorption, our own HRTF for direction, and the work moved to worker and audio threads.

### Immersive Weather & Acoustics (Valheim)

From its source (version 8.7.7):

| | Immersive Weather & Acoustics | Wire's Acoustics |
|---|---|---|
| Measured from | The character | The camera |
| "Inside" check | ~90 rays from the character, building pieces only (caves and hills don't count), every physics step (~50 per second), main thread | 64 rays from the camera, pieces, terrain, rock and static objects, 5 times a second, worker threads |
| Result | One inside/outside amount | Enclosure, room size and absorption per material |
| Reverb | Unchanged (the game's distance-only reverb) | Measured room reverb |
| Sounds behind walls | Volume only, one ray per playing sound every frame, building pieces only | Volume and low-pass by number of walls, once per sound (loops once a second, batched) |
| Rain, wind, ocean indoors | Volume multipliers (configurable down to silence) | Rain and wind: volume and low-pass by enclosure, less near openings, from their direction. Ocean: placed at the nearest shore, through walls and openings like any placed sound |
| Sounds through windows, doors and round corners | Not handled | Paths through openings, round corners and by bouncing, heard from where they come through |
| Direction | Unity panning | HRTF |
| Visuals | Hides rain and snow particles indoors, caps indoor fog | None |

Running both: set its `EnableAudioOcclusion = false` so only one audio system is active; its visual weather culling can stay on.

## Settings

- **General:** Enabled, Room reverb, Muffling, HRTF (headphones), Realistic distance.
- **Tuning:** Reverb strength, Muffling strength, HRTF strength.
- **Debug (advanced):** Debug log (measured space, most muffled sound, main-thread time, every few seconds).

## Credits

Inspired by the video [Physically-based audio: why most games sound fake [Voxel Devlog #31]](https://www.youtube.com/watch?v=oN4NZ0sDqKQ), on ray traced audio.

Disclaimer: development is AI assisted. Not fully human or AI.
Donations: ko-fi.com/krosshair
