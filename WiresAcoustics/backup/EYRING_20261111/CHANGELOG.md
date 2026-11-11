## 0.3.0
- Improved: sounds come from where they're made. The game often plays a sound at a character's feet or at the base of a piece:
  - Weapon swings and attacks come from the weapon and follow the swing. A bow's draw comes from the bow, the hammer's build sound from your hand.
  - Holstering comes from the slot on your back the item goes to, dropping from your hand, eating and drinking from your head, picking up from where the item was.
  - Creatures' growls, roars, calls and bites come from their head, their swings and claws from their weapon or arm, stomps and slams from their feet. A troll's roar and its club swing now come from two different places.
  - Chests sound from their lid, fires (sconces, hearths, braziers) from their flame, doors, smelters, fermenters and other pieces from their middle. Crafting sounds come from the crafting station instead of from you.
- Improved: anything that moves takes its sounds with it: reloading a crossbow or grappling hook, animals and pets, sounds on ships and carts.
- Improved: a ship's wake and water and other sounds outside the game's usual sound system now get the Doppler effect too.
- Removed: the "heard from its open part" backup from 0.2.7. It isn't needed now that sounds come from where they're made, and it made muffled chests sound like they were off to the right.
- Added: the wind in your ears. Outside, the wind is heard from the way it's blowing from: louder with an ear turned to it, quieter with your back to it. It fades out as you go indoors.
- Improved: every sound gets the full HRTF. Before, only the nearest few did and the rest kept only a louder near ear, so you couldn't tell where a distant fight or animal was. The simpler version saved too little to be worth it.
- Removed: the echo off cliffs and big walls in the open. It was rarely heard and cost audio processing on every nearby sound while it was on. The room's reverb is unchanged.
- Improved: how much the air dulls distant sounds now follows the international standard for it (ISO 9613-1), with the weather: the game's warm, cold and freezing are matched to the real Norwegian weather they're modelled on (a Bergen summer day and night, the mountains in winter). Sounds 10 to 60 m away were too bright before.
- Improved: rocks, furniture and tree trunks between you and a sound now muffle it by their size: the sound goes over and round them, so a trunk barely matters and a boulder takes the highs off (the same barrier formula as corners and hills, over the top and round both sides). Before they all took off the same fixed amount.
- Improved: how much a room's reverb dulls over time and how bright its reflections are now come from what the room is made of, from published absorption figures per material (stone keeps the highs ringing, wood swallows the lows), plus the air.
- Improved: sound reaching you by bouncing off a wall is now as loud as the reflection would really be: what the surface doesn't absorb, mirrored where the angle is right and scattered evenly elsewhere, by how rough each material is.
- Removed: the forest's own reverb tail. Its values weren't backed by measurements.
- Changed: the ground, trees and iron now absorb each frequency band by published data. Ground uses the Nord2000 ground classes per biome (snow, moss, grass, forest floor, sand, packed ash), trees use measured bark, iron uses steel. Before, they had one rough value for every frequency.
- Changed: how much reverb you hear now follows a published model of real rooms (Barron's revised theory), worked out from the room's size and decay and how far the sound is. The levels were measured in game. Sounds behind walls no longer get extra reverb added.
- Improved: walls, floors, roofs and doors now block sound as much as real ones measure, by their material and thickness: a wood wall like a solid timber wall (about 29 dB of the lows, 37 dB at mid frequencies), a door like a solid wood door (less, as doors leak), stone like a brick wall (far more). Through a wall you hear a muffled but still recognisable sound, not just a rumble. Hills, rocks, trees and furniture are unchanged.
- Improved: indoors, the outside (rain, wind, birds) comes in through the openings and the open sky; what gets through closed walls is a faint low rumble. With the HRTF, it now comes fully from the openings' direction, not partly.
- Improved: sound bending round a corner or through a doorway loses what real sound does (the barrier formula used for noise walls, capped at 20 dB), more of its highs the longer the detour. Before it lost about 1 dB.
- Improved: wind. Downwind a sound carries a few dB further; upwind it can drop by up to 20 dB from about 100 m away, as measured outdoors (it was at most 4 dB). The game's wind is matched to real wind speeds by its snow storm being a blizzard.
- Improved: indoors, a random bird or insect call starts outside your walls instead of somewhere in your room, so it comes in through the windows and doors from their direction.
- Improved: a hill between you and a sound is now treated as what it is, a ridge the sound goes over: the higher it rises above the line between you, the more it takes off (the same barrier formula as corners).
- Improved: how much reverb a sound gets indoors now uses the standard critical distance (from the room's measured size and reverb time) instead of a fixed guess.
- Improved: under water. Sound crossing the surface keeps about 0.1% of its energy, as physics says (about 30 dB quieter), without the old muffling, which wasn't based on anything.
- Improved: the sea is a long shoreline, so it now fades 3 dB per doubling of distance like a real line of sound, not 6.
- Improved: sound quality. The game's own mixer runs every sound effect through mid-range cuts (around 400 Hz and 1.6 kHz) and a compressor, which makes deep sounds boomy and smears their detail, and adds its own fixed reverbs; ambience also gets its treble cut above 11 kHz. Sound effects and ambience now skip those and go straight to your volume setting and the game's final clipping limit. Menus too: the mod applies the game's menu quieting itself.
- Removed: the crackle fix from 0.2.7. Repairing the clicks in the game's sound files also took detail out of many deep sounds (footsteps, landings, climbing on stone), leaving them dull and distorted at random, depending on which variant of a sound played. A worse problem than the one it fixed, so the game's sound files are left as they ship again.
- Performance: fewer raycasts for sounds of pieces and creatures, and less work every frame for the Doppler effect.

## 0.2.7
- Fixed a vanilla bug: clicks and crackles in the game's sound effects, most obvious on wood and stone footsteps because they stand out against those deep sounds. This is in Valheim's own sound files, not something this mod caused. Two things are wrong with them: many clips have tiny bursts of noise baked in, and many were trimmed so they start in the middle of the sound instead of from silence, which clicks every time they play. I tried to fix both and hopefully fully succeeded. The mod repairs the sounds in memory as the game plays them; the game's files are not changed and no audio is added to the mod.
- Improved: if the spot a sound is made at is muffled but part of the thing making it is in the open (a chest set into a wall), it's heard from that open part, so something you can clearly see is heard clearly. Sounds that aren't muffled come from where they always did.

## 0.2.6
- Sorry, I put the wrong thing in the last changelog. Claude screwed me over: the crackles on wood/stone footsteps are a vanilla bug I'm trying to fix, but he put it in there like it's my damn fault. I shouldn't trust AI with changelogs. Sorry, fellas.
- No changes to the mod itself.

## 0.2.5
- Fixed: birds, seagulls and other outside sounds came through wooden walls too clearly. A wooden wall muffles as much as it did before 0.2.3 again; stone, marble and iron still muffle more than wood.
- Fixed: random biome sounds (birds, insects) and lava noises use the same game object, so lava could be treated as background ambience. They're now told apart where the game plays them: lava stays a real sound in the world, muffled by walls.
- Fixed: looping sounds the game itself doesn't keep updating were never rechecked for walls after they started. They're now rechecked like other sounds.
- Fixed: short random ambient sounds (an insect, a splash) could be cut off by the game's fade timer before their sound ended; they now play to their natural end.
- Fixed: a sound starting while the same source still played could restart or cut the old one, and a distance-delayed sound was started and then restarted. Delayed sounds now start once, and stale delayed stops are dropped.
- Fixed: possible crackle when a sound switched between full and light HRTF, and a rare case where an echo's memory was reused while still being played.
- Fixed: a sound made right next to you (opening a gate or door, chopping, mining) was attached to your character and turned with it, so it stayed on the same side as you turned. It now stays where it was made; only your own item sounds (equipping, eating, picking up, dropping) move with you.
- Fixed: random ambience indoors (a bird, an insect) kept the left/right it started with as you turned; it now stays in place.
- Debug log: more detail on wildlife sounds, footstep clips and audio-thread cost.

## 0.2.4
- Improved: entering and leaving caves, crypts and other enclosed spaces updates room reverb much faster, without lingering wet acoustics outside.
- Improved: exterior ambience switches promptly at room boundaries instead of waiting through Valheim's long sequential ambient-loop fades. Normal weather and biome fades keep their original timing.
- Fixed: distant sounds whose fade has already reached silence now stop immediately rather than repeatedly scheduling delayed stops; pending stop requests for the same source no longer accumulate.
- Known issues: occasional clicks on constructed wood/stone footsteps and abrupt endings of some swamp random ambient effects are still under investigation. The experimental footstep attack fade and ambient-clip pitch-timing workaround were removed because they did not resolve the reported problems.

## 0.2.3
- Optimized: HRTF priority follows estimated audibility rather than distance alone; callbacks for sounds already below -80 dB bypass expensive processing if no audible delayed echo remains. New debug counters break down full/light/plain DSP and silent buffers.
- Optimized: alternative paths are scheduled only for previously blocked sounds; room second-reflection jobs run on demand. The debug log now counts actual opening, corner, first- and second-reflection winners to judge whether added complexity pays off.
- Optimized: still rooms and coastlines reuse recent measurements with periodic refreshes; doors, pieces and new dungeon rooms invalidate the geometry measurements.
- Corrected: extended sounds now base future blocked-path decisions on their least-obstructed sampled point, not always the central ray.
- Added: timber, stone, marble and metal transmit different amounts of sound using the already known collider material; reflection candidates use absorption and angular plausibility in addition to path length. Corner candidates prefer physical blocker edges over fixed arbitrary offsets.
- Fixed: switching the mod off restores vanilla reverb zones instead of losing destroyed components, and reverts modified player and random-ambience AudioSource positioning and stereo panning. Conversion backups live on per-source filters so short-lived sounds don't accumulate stale tracking entries.
- Compatibility: dungeon audio setup uses the per-room placement method rather than the two DungeonGenerator methods whose postfixes Valheim AVX displaced.
- All existing acoustic features remain; the diagnostics help identify which processing and reflection paths are actually useful. Performance improvements vary by scene.
- Note: using Windows Sonic or Dolby Atmos spatial processing alongside the mod's own HRTF can reduce its positional clarity. For best results, disable Windows spatial processing while playing Valheim (manually; no system settings are changed by the mod).

## 0.2.2
- Fixed: birds and other random ambience could play inside your house, loud and unmuffled (the game places them at a random spot around you, often in your own room). Indoors they're heard through the walls and from the windows, like rain and wind.
- Fixed: rocky valleys and ravines barely echoed: their sides counted as being indoors. Echoes now fade only under a roof.

## 0.2.1
- Fixed: the mod reported its version as 0.1.2.
- README rewritten.

## 0.2.0
- Added: sound takes time to travel (343 m/s). A sound far away starts and stops as late as its distance makes it.
- Added: Doppler. Moving sounds shift in pitch as they come and go; our own, so the camera bumping about doesn't make sounds warble.
- Added: portals carry their hum. Going through, it isn't stopped and started again: it glides to where the exit portal will be, rides with you while you load and carries on as the exit portal's hum.
- Added: forests have their own soft reverb tail from the trunks round you.
- Added: the ground counts by what it is where each ray lands (snow, grass, forest floor, mud, sand, rock, paving, tilled soil), and the sea's surface reflects sound instead of rays passing through it to the sea bed.
- Changed: no reverb from the game is used anywhere now. Outdoors is measured like any room: open ground is dry, where the game added the same long reverb everywhere (open mountains sounded wetter than a forest).
- Changed: echo per sound, from each sound's own way there and back, instead of one delay for all; loops don't echo, and how hard the surface is decides how strong it is.
- Changed: quiet sounds fade out sooner than loud ones, by the game's own range for each.
- Changed: all your own movement and action sounds come from your character and move with it; a death wail follows the falling body; a creature's facing only shapes its calls.
- Changed: creatures' and players' shapes are their bodies (a player counted as 4 m wide before).
- Changed: levels of detail for the processing: every sound is processed, the nearest get the full HRTF, the rest a lighter direction, crossfaded when they switch.
- Added: HRTF strength setting.
- Fixed: stutter spikes of a few ms, several times a second. Switching a sound's filter on and off made Unity wait for its audio mixer; filters now stay on and simply pass sounds through when there's nothing to do.
- Fixed: birds, gusts and other random ambience were far too quiet with Realistic distance (up to 20 dB); they keep the game's own fade.
- Fixed: a tick at the start of some sounds (old audio left in the delay line).
- Fixed: errors from creatures without a head point (fish, birds) stopped some sounds being checked.

## 0.1.3
- Fixed: stutter, distorted sound and hitches where a lot plays at once (a swamp in the rain), worst on slower machines. Only the nearest playing sounds are processed now, up to a count that follows how busy the audio thread really is (measured, lowered at once when it's busy); the rest are left to the game's own panning. Sounds that don't need processing no longer cost anything, and the per-sound work is faster.
- Fixed: forests and swamps sounded like a cave. Trees counted as walls: a dense swamp measured as an enclosed room (long reverb, muffled rain), every trunk muffled sounds and echoed them. Trees now scatter sound instead: they barely enclose you, muffle little and never echo.

## 0.1.2
- Added: sound reaching you by bouncing. Room rays bounce a second time off what they hit (angle in = angle out), and sounds behind walls also try the shortest bounce paths: down a corridor and round its far corner, round two corners.
- Added: echo. Out in the open, a big hard surface facing you 15 to 50 m away (a cliff, a hillside, a wall) sends sounds back from its direction, delayed by its real distance.
- Added: rain and wind indoors come from the opening's real direction with the HRTF on (behind and above as well as left and right), keeping their stereo.
- Added: the air dulls distant sounds' highs (a fight 60 m away is duller, not just quieter).
- Changed: no more pitch drop for distant sounds in the Mistlands mist (fog doesn't change pitch); they keep their reverb there too.
- Added: Wire's Stackify support. Its chest sounds after a quick stack or restock come from each chest, as world sounds.
- Changed: sounds that used to cut off at their max distance now reach twice as far and fade out there, instead of fading inside their range (a hit 30 m away is heard again).
- Optimized: the list of sounds tracked for direction and distance changes in place instead of being rebuilt; locations and dungeons get their sound filters and lose their reverb zones as they're placed, instead of scene-wide searches every few seconds.
- Changed: the HRTF's outer-ear dip is narrower and only for sounds well above or below you, so equipment and other sounds near your character aren't dulled.

## 0.1.0
- First upload: ray traced room reverb, muffling through walls, paths through openings and round corners, ambience and under-water muffling, the sea heard from the sea, HRTF, sounds following their source, your own sounds from your character, distance fade, Proximity Voice Chat support.
