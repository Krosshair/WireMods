## 0.7.0 — release candidate (rain and profiler checks pending)- **Distributed rain impact field:** Removed the assumption that outside rain is emitted by a pair of doorway sound sources. Every completed 64-ray room survey now estimates eight outside ground-impact directions from unobstructed near-horizon rays; up to four strongest regions distribute decorrelated portions of Valheim's current rain recording across the audible exterior. Rain under roofs retains its own filtered roof-impact bed. As real open exterior regions become audible, their energy trades against the 2D bed rather than stacking duplicate recordings. No new room rays, particle simulation, sound-per-drop allocation, frame hooks or coroutines. Model is a plausible virtual source field, not a literal rain-particle solver. In-game perceptual validation pending.- **Rain and general outdoor ambience through open doors:** Fixed a weather-change bug where stationary cached room geometry prevented the directional opening copies from binding to Valheim's new rain AudioClip. The existing audio update now detects the actual audible clip change and updates aperture sources using cached geometry only. The two directional slots prioritize listener-visible opening size and proximity instead of solely distant portal ray share. Rain transmits through a nearby wide-open doorway at appropriately strong, clear levels, follows a 250ms fade, and trades diffuse indoor-bed gain for directional opening gain so the same recording is not doubled at full volume. Generic ambience retains its prior slower fades. No extra raycasts, update hooks or coroutines. In-game listening still pending.- **Weapon swing source volume:** A swung weapon emits from the actual equipped visual model's cached mesh extent, centered along the weapon and following its animation, rather than from a point at the grip or the full character collider. Other hand actions, impacts, roars and footsteps retain their specific event positions. Visible weapon bounds are cached once per shown item and reused; no additional ray budget, per-frame hooks or scene scans.- **Rain responsiveness and source geometry:** Stationary listener room probes now commit newly measured roof/enclosure/wall coverage immediately instead of retaining an indefinitely partial Lerp. Rain follows those measurements in a 0.25 s continuous response, independent from the slower general ambience/reverb tail. Known emitting areas (including one-shots) are sampled within their true scaled/rotated collider boxes, not an oversized enclosing sphere. Known localized actions—weapon hands, hinges, lids, machine controls, impacts—remain point sources at their event position and no longer inherit the owner's HRTF size. Unknown sounds now default to a point instead of the arbitrary 30 cm fallback (a manual nonzero radius is still available). Same ray budget and no additional physics or frame callbacks.- **Empirical wind refraction:** Replaced the former arbitrary +3/−10 dB and 15–112 m ramps with low-cost interpolation of FHWA/NCHRP published road-traffic modeling results for different wind speeds, source-to-listener distances, hard/soft ground and receiver heights. Ground porosity and floor distance reuse existing room rays (no new physics). Measured data are for road-traffic sound with no barriers or thermal gradients, so they are a gameplay approximation—not an exact model for every creature, machine or spectrum. Wind beyond the studied 5 m/s or 488 m is clamped, not extrapolated.- **Wind-carried world audio now respects shelter:** The existing distance-dependent downwind/upwind gain applies to positioned effects (machines, fire, creatures, attacks, placed sounds and positional voice) and is scaled by the listener's *measured* room/wind exposure. Fully sheltered listeners hear no wind refraction boost/penalty; partially protected positions have less. Wind direction changes re-evaluate existing cached room rays without new physics, and regular acoustic occlusion/distance processing stays intact. This is a listener-local approximation, not a new per-source wind-ray simulation.- **Clear-weather wind ambience:** Valheim uses a separate, non-directional wind-in-reeds biome recording in Plains and other clear environments. Give that authored wind texture a broad windward HRTF/pan, existing measured upwind shelter attenuation and low-pass, and the normal wind gust response. Rain, birds and non-wind ambience remain diffuse; no extra recordings, sources or geometry scans.- **Independent looping emitters:** The game can suppress all but one nearby sound of the same type. WA now preserves independent positional fire/machine loops and starts each at a randomized sample position; distinct emitters remain audible together. Ambient/UI/voice and one-shot concurrency remain untouched.- **Fire loudness:** Relative fire-loop level derives from each original Valheim sound range, not its collider dimensions.- **Cleanup:** Removed the ineffective experimental loop start/stop fade (including extra source tails), unused mass-law helpers and inactive legacy muting/priority code. An inactive acoustic filter now passes the audio through unprocessed.- **Release checkpoint:** Automated compilation and regressions passed; indoor rain under active weather and runtime profiler performance remain to be checked before publishing.
- **0.7.0 acoustic polish revision (not yet in-game certified):** The verified single-box-wall direct path now combines a continuous projected sound-emitter area with the documented Fresnel knife-edge diffraction field instead of using a count of blocked sample rays as the final muffling amount. A barely occluded emitter keeps its clear and diffracted sound energy rather than immediately sounding like a fully blocked wall. The apparent direction can shift smoothly toward the nearest actual box edge.
- Avoided counting the same analytical wall edge twice through independent sampled corner paths, and stopped scheduling duplicate corner-probe requests while that verified analytical model is active. Complex multiple-wall, terrain and opening paths still use the existing bounded ray solver.
- **Rain in rooms:** Identify rainfall from Valheim's wet active EnvSetup and its actual currently playing AudioClip, not wetness alone. Roof impact presence follows the already measured built-roof fraction, weighted for decorative panels, while using the original game rain audio and existing separate window positions. No invented clips or extra runtime physics scans.
- **Sound source ownership:** Unparented effects are no longer attached to the first arbitrary collider in a crowded base. Only a unique exact-pivot building owner is accepted; ambiguous/overflowed source searches retain their original location. A reused static 64-collider attribution buffer avoids allocations and common 16-collider saturation.
- Added source-linked continuous movement/edge, actual weather audio-identity/roof response, and exact source-owner attribution regression tests; real-world audibility and performance remain unverified until gameplay.

- **Occlusion geometry follow-up:** World edits (building/door changes) now enqueue affected, already-playing nearby sounds immediately on the existing batched audio path; pending raycast results from old geometry are ignored, and new checks get scheduling priority. The new transmission target is applied across the following audio buffer instead of the 150 ms movement glide, without resetting audio/HRTF state.
- Removed the 0.3 obstruction-weight state gate from all multipath routing. The direct ray transmission is always applied as measured, even while edge probes are pending; missing paths are filled in when physically validated instead of preserving the previously unoccluded state.
- Added a clear-side Fresnel diffraction attenuation computed from cached, actually probed wall edges and each sound's current listener-to-source geometry. Shadow-side edge diffraction and wall-material transmission still contribute separately. More frequent rechecks (8 cm of movement instead of 50 cm) occur only near an established blocking surface.
- These are geometric transmission improvements, not proof of sample-accurate continuous ray tracing or fully independent path DSP. In-game A/B testing and profiling required, especially near wall edges and when repeatedly placing/removing building pieces.
- Corrected moving/rotated sound-source shapes: collider bounds are represented consistently in owner-local coordinates and converted into metres for source size; the facing point and chest-lid position now use the correctly rotated source box. Geometry events invalidate shape caches instead of requiring per-frame collider scans.
- Reworked Doppler smoothing to use elapsed time and an approximately 75 ms time constant, preserving the previous response near 60 FPS while eliminating frame-rate-dependent changes.
- The direction job now reports source world coordinates and the HRTF computes direction relative to the current camera pose; direct and routed sounds no longer use mismatched camera frames.
- Propagation tests all four room-detected openings, not just the first two. Multi-opening angular dispersion contributes to source width, so opposing arrivals do not collapse into a misleading point direction. All four paths remain verified by their own batched rays.
- Removed abrupt direction selection jumps at the precedence-effect transition delays near 1 ms and 50 ms, using continuous interpolation between early, precedence-dominated and separated-arrival localization.
- Corrected portal radius for openings spanning more than a hemisphere; negative radii are no longer possible.
- Ocean sheltering integrates the visibility of each detected shoreline bearing instead of one artificial average ray direction. Reuses the existing room rays and original Valheim ocean recording, without artificial cloned sound emitters.
- Removed an unused StartHeld field and kept the audio callback/DSP processing byte-for-byte unchanged. No new per-frame systems, coroutines or physics queries beyond the existing maximum four window route probes.
- Preserved intentionally compressed Valheim room dimensions and its existing audio source tuning; this build does not purport to replace the single ocean recording with independent wave events, implement a multiband transmission renderer, or complete in-game listening and performance validation.
- Fixed window-source assignment: existing ambient/rain and wind copies are matched to the nearest plausible world-space opening, not merely an opening facing the same direction. Two windows along the same wall can now keep distinct source identities when room-ray ranking changes.
- Opening sources retain their existing locations and playback for up to 0.6 seconds of missed room-ray detections instead of fading out and restarting on a single missed sample. The grace ends immediately upon a geometry-change event or when transitioning out of a room.
- Kept the current window sound samples, original clip synchronization, positional smoothing, gain transitions and all ray budgets. No new sources, physics queries or audio-processing loops.
- Correctness: if the latest shoreline scan no longer detects sea, reset obsolete ocean HRTF routing, its custom muffling and distance rolloff instead of continuing to hear the sound from the old shoreline. Vanilla remains in control of playback and its original ocean-source position.
- Correctness: sea-zone classification cache is now scoped to the current ZoneSystem and cleared on shutdown/world transition; identical zone coordinates from another world cannot reuse an old classification.
- Correctness: random outdoor bird/insect positioning now tries the remaining downward terrain samples if one candidate contains no hit, instead of abandoning the entire placement scan at the first empty sample.
- Efficiency: the monotone diffraction detour table now uses a binary lower-bound search instead of scanning up to 1,001 entries. Table values, chosen thresholds and acoustic response are unchanged.
- Blocked sound paths through two valid windows now blend their audible directions by relative arriving energy. Similar-strength paths no longer pick a left/right direction solely because one is fractionally louder. Opposite paths become more diffuse rather than arbitrarily selecting one window. The independently checked paths combine by energy (with weighted filtering), so two open windows can transmit more than one, without directly duplicating a recording's waveform.
- During wet weather, the original outdoor ambience/rain bed remains audible indoors (minimum sheltered transmission 0.72, 6500 Hz low-pass for fully sheltered wet conditions). The stronger rain bed is gated by built roof surfaces using existing room rays, so natural caves do not acquire an artificial rain layer. Rain continues to sound filtered by the structure rather than disappearing into near-silence. It uses the original game loop and retains separate window-positioned rain/wind sounds, with no extra audio sources.
- Weather-specific bed transitions interpolate over time. Ordinary non-rain ambience, wind, reverb, shoreline audio, source positions and existing ray budgets remain unchanged.

## 0.6.0
- Added back: sound takes time to travel, now at 350 m/s from your character's head. A sound far away starts as late as its distance makes it (up to half a second).
- Added back: Doppler. Sounds coming toward you are higher, going away lower, measured from your character's head (camera movement doesn't affect it).
- Performance: a new sound's wall check and a random bird or insect call's open-air spot are worked out on worker threads instead of the main thread. The sound starts once they're in (a frame later), already muffled right; that frame counts as part of its travel time. Your own sounds still start at once.
- Added: a setting for each feature: Travel time, Doppler, Under water, Air, Wind, Sound placement, Voice chat (with the existing Room reverb, Muffling, HRTF and Realistic distance).

## 0.5.12
- Station sounds come from the part you used: the smelter's coal and ore intakes and its output, the fermenter's intake and tap, the cooking station's fuel intake.
- A sound isn't muffled when you can see the side of what it comes from, even if its usual spot is covered (a chest under another in a stacked wall).
## 0.5.11
- What's in the way of a sound and the way round it (walls, windows, doorways, corners) are now worked out from the camera, like a sound's direction: you hear what you see from where you see it. How loud a sound is still goes by your character's head.

## 0.5.10
- Fixed: a voice behind a half wall started from an opening across the room (a window, a doorway) each time someone began to talk, and moved over the wall a moment later. A sound now keeps where it was heard from until every way round its walls has been looked for.

## 0.5.9
- Fixed: over a half wall, with you or the sound right by it, a sound was heard round the wall's end instead of over it.
- Fixed: the sea sounded from under you on an island, a point or a pier; it now comes from the shore's direction and sounds wider the more the sea surrounds you. Rivers and ponds no longer pull the sea's sound to them.
- Fixed: a ship or cart between you and a sound shut it out almost entirely; something in the way now muffles a sound once, not once for each of its parts.
- Fixed: fences and stake walls (stake walls, stone and wood fences, the fence gate) didn't block sound.
- Fixed: adding fuel to or lighting a small fire (a torch, a sconce) was much quieter than the game plays it.
- Fixed: the blast furnace deadened the room's reverb like a fur rug.
- Fixed: particles of an animation's sound effect could be dragged along with the moving body.

## 0.5.8
- Fixed: pieces round you could vanish and pop back in over and over (looking like the game switching detail levels) while the game streamed things in.
- Fixed: at a base full of sounds, a sound could take a second or more to change after you stepped out from behind a wall (or behind one). The loudest sounds (as loud as they'd be with nothing in the way) are now checked first, then the nearest, and a sound waiting for its check is only queued once. All of the mod's work each frame now shares one 0.5 ms budget (those checks had 0.25 ms of their own): what's needed every frame goes first, and the checks take what's left. Searching a wall for its gaps is done in small pieces, and setting up the next checks stops when the frame's time is up, so no single frame gets the whole load.
- Less work: sounds too quiet to hear even with nothing in the way aren't checked for walls, your own sounds (footsteps, swings, your gear) aren't checked at all, and longer one-off sounds are checked again only when you or they moved or something was built near them, not every half second.

## 0.5.7
- Gaps in walls are found properly: the edges of a wall (a gap beside you, a window, the end of a wall, over a half wall) are searched for along the wall itself, at the wall nearest you and the one nearest the sound. Near a gap a sound is nearly as loud as in the open and heard from the gap, and it fades in gradually as you go behind the wall instead of being muffled by the first bit of wall in the way.
- Fixed: turned or slanted walls hid the gaps beside them.
- Faster: what the mod knows about each thing a sound passes (wall, ground, tree, furniture) is worked out once and kept, in one table big enough for a large base (the old ones emptied and started over at around 8000 pieces). The edges found round a wall are kept until the wall or the building near it changes. A building change only rechecks the sounds and the room it's near: the game loads and unloads far-off pieces all the time as you move, and each one used to recheck everything. The sound processing itself runs leaner on the audio thread.

## 0.5.6
- Less lag after a portal: while the game is still loading a base in, the room is measured once when it's in instead of again for every piece that appears.

## 0.5.5
- Fixed: the wind and the outside ambience moved in audible steps as you walked (closer to a wall, through a doorway): they were set five times a second and jumped half way each time. They glide smoothly now, at the same speed.

## 0.5.4
- Fixed: a wall muffled sounds far more than real walls do. Its high end was cut for a single filter stage, but the sound runs through two, so a wall took 25 dB extra at 2 kHz and 36 dB at 4 kHz instead of the measured 16 and 20. Machines and other bright sounds (the spinning wheel) vanished behind one wall; now they come through dull, as measured. Rain, wind and the sea through walls use the same filter and get the same fix.
- Fixed: sound didn't come over a half wall. The way over a wall was tried at least 1 m above the line from you to the sound, which in a 2 m storey is in the ceiling, so it was always blocked and the sound only came through the wall. It's tried just over the wall's top edge now.
- Added: sound also finds its way under something in the way (a raised floor, a table, the gap under a gate), as it already did over and round.

## 0.5.3
- Changed: looping sounds (fires, forges, lava, machines) follow walls and doors as you move. Their wall check waited for the game's once-a-second update, so stepping through a door or behind a wall took up to a second to be heard. Now it runs when you or the sound move half a metre, a piece is built or removed, or a door opens or closes, and every 2 s otherwise.

## 0.5.2
- Fixed: walking up to a window with rain outside cut the rain in, and walking away cut it out again. The sound at each opening was tied to the opening's rank, so when a window became the bigger opening (or a new one turned up) its sound jumped across the room at full level. Each opening now keeps its own sound, a new one fades in where it is and a lost one fades out where it was. How closed in you are also glides over a second instead of stepping.

## 0.5.1
- Fixed: sound got through closed buildings. Pieces snapped together often leave a hairline gap between their colliders, and the one ray from you to a sound could run through it as if the wall were open. Every line is now three rays 8 cm apart, and what two of them agree on counts. The mod was at fault.
- Fixed: the room rays could take such a seam for a window or a door and route sounds through it. A gap counts as an opening only if the rays beside it get through too.
- Fixed: a wall right behind a sound (a torch, a chest or a workbench against the far side of a wall) didn't count: the mod ignored everything within 0.5 m of a sound so its own object wouldn't hide it. Now only the thing making the sound is left out. The mod was at fault.
- Fixed: a door's own sound could be hidden by the door: opening and closing were heard from the middle of the door's box, which for a door first seen open is out beside the doorway. They come from the hinge now, half way up. The mod was at fault.
- Fixed: a line through a busy build (walls, beams, poles, furniture) could have more hits than the mod recorded (8), and the wall could be among the ones dropped. It records 16 now, and a line with more than that counts as through a wall at least.

## 0.5.0
### Indoors and outdoors
- Changed: the sea is shaded like the wind, as the whole shoreline it is: a wall or a house between you and it still shuts most of it out, but a single post or tree no longer can (it was one ray to one point). Heard from between the shore and the way that's open to it.
- Changed: the wind is shaped by what's round you. Behind a wall, a rock or a hill it drops to a low rumble (in their lee), it's heard from where it comes round and over what's upwind, and it comes in gusts and lulls instead of one even loop, more so the harder it blows.
- Changed: the outside heard from inside, rebuilt. Rain, wind, birds and the rest of the biome's sound play from your doors and windows (up to two), just outside each one and heard from there, as loud as the opening's share of the outside (about 10 dB under outside for a door, as measured homes with a window open). Through the walls only a quiet, dull bed gets in, and openings fade in and out instead of cutting. Before, it was one flat sound from nowhere: barely muffled, or nearly unmuffled the moment any opening was found.
- Changed: you can hear through walls. One wall counts once, however many pieces it's built from (a wall, a beam and a pole on one line were three walls), and the first wall takes 12 to 22 dB by how heavy it is, each after it 6 dB more and duller. With the measured losses stacked, a hearth in the next house was at -120 dB: silent, not muffled.
- Changed: muffling is steeper (12 dB an octave instead of 6), so a sound behind a wall is muffled, not just quieter.
- Fixed: sounds outside heard from inside rang through your room's reverb as bright as if they were in it: Unity's room reverb hears a sound before the mod muffles it. A sound now gets the room's reverb only as much as gets through to you, and sounds behind walls no longer get extra reverb.
- Fixed: stepping behind something (off a wall, with the sea behind it) shut a sound off for a moment before its way round was found, then opened it again. The way round is found first now.
- Fixed: the game's random calls (birds, gulls, insects) are placed at your own height, so in a basement or on a hillside they were in the ground, and one could land on your roof and sound like it was in the room. They sit in the open air above their spot, off buildings.
- Fixed: a door or window looking out at a hill or trees didn't count as an opening, and a seam in the roof could.
- Fixed: sounds already playing could be muffled by walls that weren't there: their checks read leftover hits from earlier ones (Unity only clears the first unused slot). The mod was at fault.
- Fixed: a sound behind a wall could seem to come from inside your room. Where you hear it from follows the precedence effect: the sound through the wall arrives first and sets the direction, and a window or corner only takes over when it's more than 10 dB louder.
- Improved: a sound coming through an opening bends round its nearest edge, not its middle, and loses nothing extra when the straight line passes through it (Kurze and Anderson, the lit side of an edge).

### Reverb
- Changed: rooms sound like the real rooms they stand for. Valheim's buildings are about twice real size and bare, so they rang like halls; the reverb treats them as half the size and furnished, with the warmer tone from 0.1. Indoors it's cozier the more closed in you are: softer first reflections, a darker ring.
- Improved: stepping out of a space, its reverb dies away over that space's own reverb time (a cave's over seconds, a small room's at once).

### Distance and loudness
- Changed: which way a sound is comes from the camera, as you see it; how far it is, what's in the way, which room you're in and whether you're under water come from your character's head again, where the game itself hears from (0.4.0 had moved all of it to the camera). Zooming out no longer makes your own steps quieter, and the camera slipping behind a wall no longer muffles the world.
- Changed: sounds reach further, just quieter. Past the game's own range for a sound it drops 6 dB more each time the distance doubles, instead of fading out to nothing.
- Changed: fires are as loud as how much is burning. The game plays a sconce's crackle as loud as a hearth's; now a sconce is about 14 dB quieter than a campfire.
- Fixed: quieter sounds far off cut out as you walked away (a mead ketill across the hall), and with more sounds than voices Unity cuts an old sound off or doesn't start a new one. Unity mixes only 32 sounds at once in Valheim and a busy base plays about 60: the mod asks for 128, so every sound plays to its end, and sounds too quiet to hear give way first if even that runs out.
- Changed: sounds upwind of you drop by at most 10 dB instead of the measured 20 and more, so a sound you can see doesn't all but vanish in a breeze. A choice for the game, not from a source.
- Removed: sounds arriving late with distance, and the Doppler shift.

### Other
- Improved: a falling tree is heard from its trunk as it comes down, not from the stump.
- Fixed: a portal's hum dipped out for a moment as it was handed to the exit portal, which often doesn't count itself in use yet when you land. It's held on through the hand-over.
- Changed: pausing (the Esc menu) keeps the world sounding as it is, instead of switching to the menu's muffled mix.
- Fixed: switching the mod off left the reverb send it had set on sounds already playing; the game's own is put back.
- Improved: with Proximity Voice Chat, a voice comes from the speaker's head (it was 1.5 m above their feet, wrong when they crouch, sit or swim).
- Fixed: with your head under water, sounds under water with you were muffled as if they came through the surface.
- Fixed: on 5.1 and 7.1 speakers with the HRTF on, the centre and rear speakers went silent. The game's own panning is used there now.
- Fixed: a few material values didn't match the sources they're credited to: marble, ice, plank and heavy wood now absorb what the published tables say, and iron and ice walls follow the mass law as published.
- Removed, to keep the mod to what clearly matters: sounds reaching you by bouncing off walls (openings and corners cover it), and a creature's voice being duller from behind it.
- Performance: sounds too quiet to hear (under -60 dB where you are) aren't processed at all; every other sound gets the full HRTF. Distance, air and wind are worked out 4 times a second instead of every frame.
- README: every source links to where its numbers come from, and the values that don't come from a source are listed as such.

## 0.4.1
- Fixed: an endless loading screen with errors in the game's object clean-up (ZNetScene.RemoveObjects), since 0.3.0. The mod attached creature and weapon sounds to the creature so they'd move with it; in some setups those sounds are networked objects, and they were destroyed together with the creature while the game still tracked them. The mod was at fault. Networked sounds now follow their creature without being attached.
- Fixed: the Room reverb setting's description said the game's own reverb comes back outdoors. It doesn't: the mod's reverb is used everywhere and goes dry in the open.

## 0.4.0
- Changed: you now hear from the camera, not from your character. Left and right, near and far, and which room you're in follow your view.
- Fixed: a room's size counted the sky or the hills seen through its windows, doors and gaps, so rooms seemed several times bigger than they are.
- Improved: every sound gets the full HRTF. Before, only the nearest few did and the rest kept only a louder near ear, so you couldn't tell where a distant fight or animal was. The simpler version saved too little to be worth it.
- Removed: the echo off cliffs and big walls in the open. It was rarely heard and cost audio processing on every nearby sound while it was on. The room's reverb is unchanged.
- Improved: how much the air dulls distant sounds now follows the international standard for it (ISO 9613-1), with the weather: the game's warm, cold and freezing are matched to the real Norwegian weather they're modelled on (a Bergen summer day and night, the mountains in winter). Sounds 10 to 60 m away were too bright before.
- Improved: rocks, furniture and tree trunks between you and a sound now muffle it by their size: the sound goes over and round them, so a trunk barely matters and a boulder takes the highs off (the same barrier formula as corners and hills, over the top and round both sides). Before they all took off the same fixed amount.
- Improved: how much a room's reverb dulls over time and how bright its reflections are now come from what the room is made of, from published absorption figures per material (stone keeps the highs ringing, wood swallows the lows), plus the air.
- Improved: sound reaching you by bouncing off a wall is now as loud as the reflection would really be: what the surface doesn't absorb, mirrored where the angle is right and scattered evenly elsewhere, by how rough each material is.
- Removed: the forest's own reverb tail. Its values weren't backed by measurements.
- Changed: the ground, trees and iron now absorb each frequency band by published data. Ground uses the Nord2000 ground classes per biome (snow, moss, grass, forest floor, sand, packed ash), trees use measured bark, iron uses steel. Before, they had one rough value for every frequency.
- Improved: walls, floors, roofs and doors now block sound as much as real ones measure, by their material and thickness: a wood wall like a solid timber wall (about 29 dB of the lows, 37 dB at mid frequencies), a door like a solid wood door (less, as doors leak), stone like a brick wall (far more). Through a wall you hear a muffled but still recognisable sound, not just a rumble. Hills, rocks, trees and furniture are unchanged.
- Improved: indoors, the outside (rain, wind, birds) comes in through the openings and the open sky; what gets through closed walls is a faint low rumble. With the HRTF, it now comes fully from the openings' direction, not partly.
- Improved: sound bending round a corner or through a doorway loses what real sound does (the barrier formula used for noise walls, capped at 20 dB), more of its highs the longer the detour. Before it lost about 1 dB.
- Improved: wind. Downwind a sound carries a few dB further; upwind it can drop by up to 20 dB from about 100 m away, as measured outdoors (it was at most 4 dB). The game's wind is matched to real wind speeds by its snow storm being a blizzard.
- Improved: indoors, a random bird or insect call starts outside your walls instead of somewhere in your room, so it comes in through the windows and doors from their direction.
- Improved: a hill between you and a sound is now treated as what it is, a ridge the sound goes over: the higher it rises above the line between you, the more it takes off (the same barrier formula as corners).
- Improved: how much reverb a sound gets indoors now uses the standard critical distance (from the room's measured size and reverb time) instead of a fixed guess.
- Improved: under water. Sound crossing the surface keeps about 0.1% of its energy, as physics says (about 30 dB quieter), without the old muffling, which wasn't based on anything.
- Improved: the sea is a long shoreline, so it now fades 3 dB per doubling of distance like a real line of sound, not 6.
- Removed: the crackle fix from 0.2.7. Repairing the clicks in the game's sound files also took detail out of many deep sounds (footsteps, landings, climbing on stone), leaving them dull and distorted at random, depending on which variant of a sound played. A worse problem than the one it fixed, so the game's sound files are left as they ship again.
- Performance: the audio processing no longer times itself unless the debug log is on.

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
