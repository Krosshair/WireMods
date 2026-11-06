# Wire's Acoustics

Ray traced room acoustics, occlusion and an HRTF for Valheim's audio. Client side.

## Video

Coming later (not recorded yet).

## How it works

Everything is measured from the audio listener (the camera), never from the character.

### 1. Room measurement

- Every 0.2 s, 64 rays in evenly spread directions (Fibonacci sphere) from the listener, up to 50 m, against building pieces, terrain, rock and static objects. Cast as one Unity `RaycastCommand` batch on worker threads; the results are read on the next tick.
- From the first hit of each ray:
  - **Enclosure:** share of level and upward rays that hit something.
  - **Mean free path:** average hit distance (the room's size as sound sees it).
  - **Absorption** (share of sound energy a surface swallows) by what was hit:

    | Surface | Absorption |
    |---|---|
    | Stone, ashstone, ice | 0.03 |
    | Marble | 0.02 |
    | Iron | 0.04 |
    | Ancient | 0.05 |
    | Hardwood, timber | 0.09 |
    | Wood | 0.12 |
    | Furniture, stations, racks, props (non-building pieces) | 0.30 |
    | Beds, banners, carpets (the game's comfort groups), rugs, hides, cloth | 0.55 |
    | Terrain (soil, grass) | 0.35 |
    | Rock | 0.04 |
    | Trees, other | 0.15 |
    | Ray that hit nothing (open sky, doorway) | 1.0 |

    Building pieces count as walls by their build category (the hammer's building tabs); everything else is furniture.
- **Decay time** from Sabine's formula, RT60 = 0.161 V / (S a). With the mean free path l = 4V/S this is RT60 = 0.04 l / a.
- **Reverb level** from the room constant, proportional to (1 - a) / a: a bare stone room rings, a furnished one barely does.
- **Early reflection delay** from the nearest hits, **late reverb delay** from the mean free path.
- These drive one `AudioReverbZone` that follows the listener. The game's own reverb zones (the main one is a big 4 s reverb raised by distance only; any other area's zone, a dungeon's, goes the same way) are removed while the mod is on, and the main one's settings are used as the "fully outdoors" end: the result is blended toward the measured room by how enclosed you are. Values are only written when they change audibly. The game's other built-in reverb, its one fixed "Indoor" mix under any roof, is replaced by the measured room while Room reverb is on (still used inside a shield dome).

### 2. Reverb amount per sound

The game raises each sound's reverb send with distance only (distance / 64 m). Here, indoors, it follows how a real room works: near sounds are mostly direct, sounds past the room's critical distance mostly reverb. Send = d / (d + r), with r = 2 m + 10 m x absorption, blended with the game's value by enclosure.

### 3. Occlusion (sounds behind walls)

- One ray from the listener to each sound when it starts. Looping sounds are rechecked once a second, and one-off sounds still playing after 0.4 s (a long roar moving behind a wall) twice a second, batched on worker threads (8 hits per ray).
- Everything the ray passes counts toward "walls in between": walls, floors, roofs, beams, terrain and rock 1, furniture 0.25, trees and other objects 0.3. The object the sound comes from is skipped (a chest, a workbench, anything the sound sits inside), but a wall a sound is mounted on (a wall torch) still counts from its other side. The last 0.5 m before the sound is skipped.
- Walls in between set volume and a low-pass cutoff, interpolated:

  | Walls | Volume | Low-pass |
  |---|---|---|
  | 0 | 100% | none |
  | 1 | 60% | 1500 Hz |
  | 2 | 40% | 800 Hz |
  | 3+ | 28% | 500 Hz |

  Occluded sounds also get a larger reverb share (what reaches you comes around the walls).
- Applied in a filter on the sound itself (`OnAudioFilterRead`, audio thread): gain and a one-pole low-pass, gliding over about 0.15 s so nothing jumps or clicks (a sound that's just starting takes its value at once).

### 4. Paths around walls: openings, corners and bounces

- **Finding openings:** inside an enclosed space (enclosure 35% or more), a level or upward room ray that hits nothing left through an opening: a window, a door, a hole in the roof. Neighbouring escaped rays form one opening. It's placed in their average direction at the distance of the walls around it (the hits of the neighbouring rays), and its share of the rays is how big it looks from where you are. Up to 4 openings are kept, biggest first.
- **Paths through them (diffraction):** a sound with walls in the direct line is also tried through the 2 biggest openings: listener to opening (clear, that's how it was found), opening to sound. A path's walls are what lies between the opening and the sound, plus a bend penalty (sound loses energy turning a corner: 0 straight on, 0.3 walls at 90 degrees, 0.6 turning back), and its volume drops with the extra distance (direct distance / path length). The quieter of the direct line and the best path wins.
- **Round corners and over walls:** where there's no opening (outdoors behind a house, a partition indoors), five points around the first thing in the way are tried: 2.5 m and 5 m to each side and 3 m above. Each path is listener to point to sound, costed the same way (walls on both legs, bend, extra distance).
- **Bounces (two reflections):** each room ray that hit something bounces once more, mirrored off the surface it hit (angle in = angle out, by the surface's normal), on worker threads. A sound with walls in the way also tries the three bounce points with the shortest way to it: listener to first bounce (to second bounce) to sound. Cost: walls on the last leg, what each surface swallows (its absorption x 1.5), and how far the last leg turns off the mirror direction (0 on it, 0.5 walls at 90 degrees, 1 turning straight back). That finds the way down a corridor and round its far corner, or round two corners. Heard from the first bounce.
- Through an opening or round a corner, the sound is heard from where it comes through: the HRTF places it at the window, the corner or the bounce, not at the source behind the wall.
- Paths are all cast on worker threads. A new sound behind walls starts muffled (its direct ray is checked at once) and opens up a frame or two later if a path is better.

### 5. Positionless ambience

The game's 2D ambient sounds (the environment loop with its birds, insects and rain, plus wind and the random ambient calls) have no position to trace to. They're muffled by how enclosed you are (enclosure squared), down to 30% volume and a 700 Hz low-pass when fully enclosed, and keep their own stereo panning. Openings let the outside in: the bigger and nearer an opening, the less muffled, and the environment loop and wind lean toward it, so rain indoors comes from the window. With the HRTF on, that's the opening's real direction (behind or above you as well as left or right): the HRTF on the loop's centre, its stereo width kept, up to 80% the more closed in you are. Without it, stereo pan up to 80% toward the openings' side. Random ambient calls (birds, insects) starting indoors lean the same way, on top of their own left/right placement (a call already on the left stays left).

### 6. Echo

Out in the open, a big surface facing you 15 to 50 m away (a cliff, a hillside, a wall) sends sounds back from its side, 2 x distance / 343 m/s later. From the room rays: hard surfaces hit nearly head-on (the angle each ray hit at counts), at least two rays on it, the strongest by facing x hardness / distance. Every placed sound gets a delayed copy from that direction: constant-power panned, low-passed (4 kHz, duller from behind), level from the surface's hardness and the extra distance, scaled by Reverb strength. Fades out the more enclosed you are, where the room's reverb is what you hear.

### 7. The sea

Vanilla plays the ocean loop at the average centre of the ocean zones (64 m squares) around you, mostly unplaced and spread wide. Here, once a second, rays straight down at 96 points around the listener (16 directions, 10 to 110 m, on worker threads) find deep water (ground 6 m or more under sea level). The loop moves to the nearest shore in that direction (each direction's first deep-water point, weighted by 1 / distance squared) and is then handled like any placed sound: walls, paths through openings and the HRTF. The game still decides its volume.

### 8. Under water

- Camera under the surface: everything is muffled (45%, 400 Hz).
- A sound from under the surface is muffled the same way, only if the whole source is under: for a creature its head must be under (a swimming greydwarf with its head up is not), anything else 30 cm under. Sounds well above sea level skip the water check.

### 9. HRTF (headphones)

Unity's own panning is flattened (spread 180) and every placed sound gets, per ear:

- **Interaural time difference:** Woodworth's formula, (a / c)(theta + sin theta), head radius 8.75 cm, up to about 0.66 ms, as a fractional delay on the far ear.
- **Head shadow:** the far ear's highs roll off (low-pass from 20 kHz down to 1.8 kHz fully to the side) and it loses up to 30% level; the near ear gains up to 12%.
- **Front/back:** from behind, the highs above about 3.5 kHz are shaded on both ears.
- **Elevation:** the outer ear's notch, a peaking filter moving from about 5 kHz (below) to 10 kHz (above), narrow (Q 4, like a real pinna notch), and only for sounds well above or below ear level (none within 12 degrees, -9 dB from 44 degrees), so sounds in front and your own character just below the camera aren't dulled.
- **Wide sources stay wide:** the HRTF is scaled by how wide the game made the source (its own spread, before the game widens it with distance): a point source gets full direction, a source spread all around (a waterfall, a shield dome) gets little.
- Stereo clips keep their width (their side signal is added back).

Directions are computed each frame by a read-only Unity transform job on worker threads; the main thread only copies the results.

### 10. Main menu

The menu scene's own placed sounds (the campfire and the rest) get the HRTF too, and the room measurement runs there like anywhere else.

### 11. Other placed sounds

Some placed sounds don't use the game's sound-effect system: ship wake and water sounds, in-water splashing, the shield generator's hum, the Ashlands burn sounds, and other mods' own sources. Their prefabs get the filter at load, like sound effects; locations and dungeons, loaded as you get near, get theirs as they're placed (and lose their reverb zones then too). They get the HRTF (by their width) and wall, opening and corner checks twice a second on worker threads. Music and interface sounds are left alone.

### 12. Sounds that move with their source

The game spawns many effect sounds at a point and leaves them there: a creature's idle and alert sounds, hits, jumps, deaths. A troll's roar would stay where it started while the troll walks on. Here a sound spawned for a creature or player keeps its place on it until the sound ends or its owner is gone. Sounds the game already attaches are left as they are.

### 13. Your own sounds

The game plays many player sounds unplaced, so they sound like they're in the camera: equipping, moving, drawing a bow, reloading a crossbow, staff casts. Any unplaced sound on a player, or spawned right at one, is placed at that player and follows them, with the HRTF, walls and the room's reverb like any sound. Its near distance is raised to a third-person camera's distance, so it isn't quieter than before. Interface sounds and the game's cues that aren't sounds in the world (skill level-up, the adrenaline and perfect dodge cues) stay as the game made them.

### 14. Distance: range and air

Unity's logarithmic rolloff doesn't reach silence at a sound's max distance, it cuts off there: a sound walking out of range drops from quiet to nothing. Those sounds reach twice as far instead, getting quieter with distance the same way, and fade out smoothly over that extra range (distance read on worker threads every frame): as loud as the game made them within their own range, with a gentle tail past it.

**Air:** the further a sound travels, the more of its highs the air swallows, so a fight 60 m away is duller, not just quieter. A low-pass on every placed sound by its distance: none near you, about 15 kHz at 30 m, 9 kHz at 60 m, 4.5 kHz at 100 m, never below 2.5 kHz.

**Mist:** inside the Mistlands mist the game lowers a sound's pitch with distance (down to half at 64 m) and takes its reverb away. Fog doesn't do that to sound (droplets that small barely touch it), so in the mist sounds behave as anywhere else: the pitch drop is undone and the game's distance reverb is put back.

### 15. Proximity Voice Chat (Azumatt)

Voices are their own audio sources, so they're handled directly: HRTF while placed (normal panning while global), occlusion and paths through openings 5 times a second (batched), under-water muffling by the speaker's head. Its own occlusion and under-water muffle are held open while this mod is on, so nothing applies twice. Its "Voice Reverb" option is left as it is.

### 16. Wire's Stackify

Its chest sounds after a quick stack or restock come from each chest itself, on the chest's own world sound channel (Stackify alone places them 1.5 m from the camera on the interface channel), so they're heard through walls and openings, with the room's reverb and the HRTF.

## Where the work runs, and the cost

| Work | Thread |
|---|---|
| Room rays and their second bounces, sea rays, all paths round walls, rechecks of playing sounds and voices, HRTF directions | Unity worker threads (jobs) |
| Muffling and HRTF per sample | Audio thread |
| One occlusion ray per new sound, finding openings, moving sounds with their owner, applying results, reverb values when they change | Main thread |

Measured with a main-thread profiler in a lived-in base: **about 0.1 ms per frame** on the main thread with everything above on (0.098 ms in the latest capture, 0.5% of a 19 ms frame; 0.025 to 0.036 ms before bounces, echo, air and sounds following their source were added). Its slowest single frame in that capture was under 1 ms. Sound starts cost a few microseconds each.

| Without the mod | With it |
|---|---|
| 60 fps (16.7 ms) | 59.6 fps |
| 144 fps (6.9 ms) | 142 fps |

Work on the audio thread doesn't hold up frames; if it ever overloaded, you'd hear crackling, not lose fps.

## Compared to other mods

### Sound Physics Remastered (Minecraft)

[Sound Physics Remastered](https://github.com/henkelmax/sound-physics-remastered) provides "realistic sound attenuation, reverberation, and absorption through blocks" for Minecraft by tracing sound bouncing through the world. Same family of idea. Here: two bounces per ray (for paths) plus the room acoustics formula (for the reverb) instead of many bounces, per-material and per-furniture absorption, our own HRTF for direction, and the work moved to worker and audio threads.

### Immersive Weather & Acoustics (Nexus 4161)

From its source (version 8.7.7):

| | Immersive Weather & Acoustics | Wire's Acoustics |
|---|---|---|
| Measured from | The character | The camera |
| "Inside" check | ~90 rays from the character, building pieces only (caves and hills don't count), every physics step (~50 per second), main thread | 64 rays from the camera, pieces, terrain, rock and static objects, 5 times a second, worker threads |
| Result | One inside/outside amount | Enclosure, room size and absorption per material |
| Reverb | Unchanged (the game's distance-only reverb) | Measured room reverb |
| Sounds behind walls | Volume only, one ray per playing sound every frame, building pieces only | Volume and low-pass by number of walls, once per sound (loops once a second, batched) |
| Rain, wind, ocean indoors | Volume multipliers (configurable down to silence) | Rain and wind: volume and low-pass by enclosure, less near openings, leaning toward them. Ocean: placed at the nearest shore, through walls and openings like any placed sound |
| Sounds through windows, doors and round corners | Not handled | Paths through openings and round corners, heard from where they come through |
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
