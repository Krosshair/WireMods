# Wire's Acoustics

Ray traced acoustics for Valheim: the space around you shapes every sound. Client side.

Everything is heard from the camera, never from the character.

## Video

Coming later (not recorded yet).

## Features

- **Room reverb, measured.** 64 rays from the camera measure the space five times a second: how enclosed it is, how big, and what it's made of. Sabine's room formula turns that into the reverb, so a stone hall rings, a log cabin is warmer, and a cave sounds like a cave.
- **Forests aren't rooms.** Trees scatter sound instead of reflecting it: a dense swamp doesn't sound like a cave, trunks muffle little and never echo.
- **Clutter counts.** Furniture breaks up and swallows reflections, and soft things (beds, rugs, banners) swallow the most. A decorated room sounds lived in; an empty one echoes.
- **Muffling through walls.** One ray from you to each sound counts what's in between: walls, floors, hills and rocks count fully, furniture and trees partly, and the object making the sound doesn't count. More layers make the sound quieter and duller.
- **Sound finds its way round.** A blocked sound also tries paths through the openings you're near (windows, doors, holes in the roof), round the corners of what's in the way, and by bouncing off up to two surfaces. The best path wins and the sound is heard from where it comes through: a deer outside is heard at the window, not through the wall.
- **Echo.** Out in the open, a cliff, hillside or wall facing you sends sounds back from its side, delayed by its real distance.
- **HRTF (headphones).** Our own model of how your head and ears hear direction: a tiny delay and dulling for the far ear, duller sounds behind you, and the outer ear's cue for above and below. You can tell front from back and up from down, not just left from right. Wide sources stay wide.
- **Rain and wind indoors.** Muffled by how closed in you are, less near openings, and coming from the opening's direction: rain indoors comes from the window.
- **The sea, from the sea.** The ocean loop is placed at the nearest real shore, so it's on your left when the water is, and walls and openings shape it like any sound.
- **Under water.** With the camera under the surface everything is muffled. Sounds whose source is fully under water are muffled for you above it.
- **Distance.** The air dulls a sound's highs the further it travels, so a fight 60 m away is duller, not just quieter. Sounds that used to cut off at their range limit now fade out over twice the distance.
- **Sounds that move with their source.** A creature's roar, a hit or a death sound follows the creature instead of staying where it started.
- **Your own sounds, from you.** Equipping, moving, drawing a bow, reloading, staff casts: the game plays many of them "in the camera"; here they come from your character. Skill level-ups and the interface stay as they are.
- **Mist.** The game drops the pitch of distant sounds inside the Mistlands mist. Fog doesn't do that to sound, so here the mist sounds like anywhere else.
- **Main menu.** The menu's own sounds get the HRTF too: a first taste before you load in.
- **Replaces the game's reverb.** The game's one distance-only reverb zone and its fixed "Indoor" mix under any roof are replaced by the measured space (the Indoor mix is still used inside a shield dome).

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

Audio-thread work is budgeted. Only the nearest playing sounds (up to 32) get the processing, fewer if the audio thread gets busy (it's measured), so a crowded swamp in the rain can't overload it; the rest keep the game's own panning.

## The numbers

- **Room:** 64 directions, 50 m. Enclosure is the share of level and upward rays that hit something. RT60 = 0.04 x mean free path / absorption. Reverb level follows the room constant (1 - a) / a. Absorption: stone 0.03, marble 0.02, iron 0.04, hardwood 0.09, wood 0.12, furniture 0.3, soft furniture 0.55, soil 0.35, rock 0.04; open sky absorbs fully.
- **Reverb send per sound:** d / (d + r), with r = 2 m + 10 m x absorption (near sounds mostly direct, far ones mostly room), blended with the game's own by enclosure.
- **Walls:** 0 = 100%, 1 = 60% and 1500 Hz, 2 = 40% and 800 Hz, 3+ = 28% and 500 Hz, interpolated. Changes glide over about 0.15 s; a sound starting takes its value at once. Looping sounds are rechecked once a second, long one-off sounds twice a second.
- **Paths:** openings are clusters of escaped room rays (up to 4 kept, the 2 biggest tried). Corners: 2.5 m and 5 m to each side of the first blocker and 3 m above. Bounces: each room ray reflects once more (angle in = angle out), the 3 shortest bounce paths tried. Cost: walls on the way + bend (0.3 walls at 90 degrees) or reflection (absorption, and how far off the mirror direction), volume by direct / path length.
- **Echo:** surfaces 15 to 50 m away hit nearly head-on by at least two rays; delay 2 x distance / 343 m/s, low-passed at 4 kHz (duller from behind), fades out as you go indoors, scaled by Reverb strength.
- **HRTF:** Woodworth's interaural delay (head radius 8.75 cm, up to 0.66 ms), far-ear low-pass down to 1.8 kHz, highs shaded behind you above 3.5 kHz, a narrow outer-ear notch from 5 kHz (below) to 10 kHz (above) only for sounds well above or below ear level. Scaled down for wide sources.
- **Ambience:** volume down to 30% and a 700 Hz low-pass fully enclosed, opened up by nearby openings; leans up to 80% toward them.
- **Sea:** 96 downward rays once a second (16 directions, 10 to 110 m) find water 6 m below sea level; the loop moves to the nearest.
- **Under water:** 45% and 400 Hz.
- **Air:** low-pass about 15 kHz at 30 m, 9 kHz at 60 m, 4.5 kHz at 100 m, never below 2.5 kHz.

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

- **General:** Enabled, Room reverb, Muffling, HRTF (headphones).
- **Tuning:** Reverb strength, Muffling strength.
- **Debug (advanced):** Debug log (measured space, most muffled sound, main-thread time, every few seconds).

## Credits

Inspired by the video [Physically-based audio: why most games sound fake [Voxel Devlog #31]](https://www.youtube.com/watch?v=oN4NZ0sDqKQ), on ray traced audio.

Disclaimer: development is AI assisted. Not fully human or AI.
Donations: ko-fi.com/krosshair
