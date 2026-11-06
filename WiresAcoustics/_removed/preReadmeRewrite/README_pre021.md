# Wire's Acoustics

Sound physics.

Inspired by the video [Physically-based audio: why most games sound fake [Voxel Devlog #31]](https://www.youtube.com/watch?v=oN4NZ0sDqKQ), on ray traced audio.

## Showcase video

Coming later sorry lol. If you want, you can record your own I guess.

## How it works

The mod traces rays through the world, the way light is traced in a renderer, but for sound. A few times a second, rays go out from the camera in every direction and find out what's around you: how closed in you are, how big the space is, what it's made of, and where the openings are. Every sound you hear is then shaped by that space and by everything between it and you.

## Features

### Reverb from the real space
It's worked out from what the rays hit, so a stone hall rings, a log cabin sounds warm, a cave sounds like a cave, and a furnished room sounds lived in while an empty one echoes. Outdoors is measured the same way: open ground is dry, and a forest has its own soft tail from the trunks around you. The game's fixed static reverb is replaced everywhere.

### Muffling through walls
Walls, floors, hills and rocks between you and a sound make it quieter and duller, more so through several layers. Furniture and trees muffle a little.

### Sound finds its way round
A sound behind a wall also reaches you through the openings: a window, a door, a hole in the roof, round a corner, or bouncing off a surface. You hear it from where it comes through, so a deer outside is heard at the window much more clearly than through the wall.

### Echo
Out in the open, a cliff or a wall facing you sends sounds back from its side, a moment later. Bare rock echoes clearly; a snowy slope barely does. It's barely noticeable as most situations in Valheim won't have you facing huge walls. I haven't tested there, but I can guess that this would be pretty noticeable next to a fortress in the Ashlands, just speculating though.

### Material matters
Snow swallows sound, grass and forest floors soften it, stone and paving reflect it, and water reflects almost everything. Just like irl, cool right.

### Sound takes time
Sound travels at the speed of sound. A tree falling far away is heard after you see it.

### Doppler
Moving sounds rise in pitch as they come toward you and drop as they go away.

### Distance
Sounds fade with distance the way real sound does, instead of cutting out after a few metres. Quiet sounds fade sooner than loud ones, the air takes the highs off far sounds, and the wind carries sound downwind.

### HRTF (headphones)
HRTF stands for head-related transfer function: how your head and ears change a sound depending on where it comes from. A sound on your left reaches your right ear a tiny bit later and duller, sounds behind you lose some brightness, and your outer ear colours sounds from above and below. The mod models this itself, so you can tell front from back and up from down, not just left from right. Best with headphones, *I'd recommend turning HRTF off on speakers, as your real head will already produce ACTUAL hrtf.*

### Sounds have a place and a shape
Creature sounds follow the creature, a death wail follows the falling body, and a creature's calls are duller when it faces away. Big sources (a hearth, a cart, a troll) sound as wide as they look and are only hidden when all of them is. Your own sounds (steps, equipping, drawing a bow) come from your character, even the ones the game plays "in the camera". Point of this being, it's not all just coming from a single point and it moves with the source, unlike vanilla.

### Weather and the sea
Rain and wind indoors come muffled through the walls and clearer from the window. The sea is heard from the actual shore. Underwater, everything is muffled.

### Portals
Going through a portal, its hum doesn't stop and restart: it carries through with you and keeps playing from the exit portal. This is more of an extra feature and not really a sound physics thing but I added it anyway, since it's audio related.

### Also
- No pitch drop in the Mistlands mist (fog doesn't do that to sound and I don't like it).
- The main menu's sounds get the HRTF too. Barely noticeable there tbh but it's there.

## Works with

- **Proximity Voice Chat (Azumatt):** voices get the walls, openings and HRTF like any sound.
- **Wire's Stackify:** its chest sounds come from each chest.
- (kinda) **Immersive Weather & Acoustics:** set its `EnableAudioOcclusion = false` so only one audio system runs; its visual weather can stay on.

## Performance

The ray tracing runs on Unity's worker threads, the per-sound processing on the audio thread, and the main thread only does a small amount of bookkeeping: about 0.1 ms per frame. Sounds have varying LODs: the nearest get the full HRTF, the rest a lighter version you can't tell apart at that distance, and the count adjusts to how busy the audio thread actually is (This is to keep the game fully performant on low end systems but said systems might experience a worse version of the mod's potential audio)

## Settings

- **General:** Enabled, Room reverb, Muffling, HRTF (headphones), Realistic distance.
- **Tuning:** Reverb strength, Muffling strength, HRTF strength.
- **Debug:** Debug log.
- note that the settings will be expanded upon eventually.

Disclaimer: development is AI assisted. Not fully human or AI.
Donations: ko-fi.com/krosshair
