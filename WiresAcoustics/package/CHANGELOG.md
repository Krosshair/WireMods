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
