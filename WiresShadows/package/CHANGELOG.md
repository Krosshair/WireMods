## 0.2.0
- Faster moving shadows: something moving is only drawn into a light's shadow when that shadow can show: on screen, and where the light reaches it (not behind a wall from it). Each redraw also skips a copy it used to make.
- Faster point lights: their lighting pass keeps the GPU's early depth test again (only the experimental Tiled lights need to switch it off).
- Faster sun lighting: the sun's pass no longer carries the point light shadow code and no longer waits on its own shadow before reading the scene.
- Shadows load during the loading screen, around where you spawn. Anything left is finished the frame you arrive, before the screen fades, after a world load, a respawn or a teleport. No shadows popping in afterwards.
- What you do comes first: placing or breaking pieces, doors, items on stands and placing, taking or eating food. The light brightest where it happened updates its shadow at once; the other lights whose shadow of it you can see follow first in line. The world streaming in around you waits behind them.
- A piece you place casts its shadow at once (as a moving shadow until its lights' cached shadows take it in).
- Changes next to lights you walk around show right away: a light whose shadow was being resized no longer makes them wait for the whole resize, and a resize that was under way can't bring the old shadow back.
- A dropped or falling item's shadow follows it every frame until it settles.
- Placed food is drawn into shadows the same way as building pieces.
- Fixed: a food building preview (holding food to place it) left a shadow until something else was placed.
- The building preview casts no shadows, as in vanilla.
- New: Quick benchmark (General): the mod against vanilla in about 10 seconds. Benchmark now takes the middle frame time of each part (a stutter doesn't skew it) and shows what moving shadows, point lights and the lighting shader each cost.
## 0.1.11
- New: Flicker culling (General, on by default). Lights more than 15 metres from you stop flickering, as do lights whose entire reach is out of view. Their brightness holds steady until nearby and visible again; fades and flashing-light accessibility settings remain supported. The distance is adjustable.
- Fixed: A placed feast or other food could leave its shadow behind briefly after being broken or removed. Visible affected faces now get priority within the existing redraw budget, without scanning all nearby geometry again.
- Less CPU work around large moving objects: oversized moving shadow casters no longer expand the search around every light. The original shadows are preserved.
- More reliable cached shadows: a failed cube face is no longer marked finished, and temporary rendering settings are restored even if a render fails.
- The experimental indirect renderer is not included in this stable release. Tiled lighting remains optional and unchanged.
## 0.1.10
- Less lag placing and breaking: the shadow updates round a change no longer all run inside that one frame (up to 12 ms seen); a little each frame instead, nearest first. Lights out of view update when they come into view.
- Fixed: picking up or eating placed food lagged like placing a feast did: every light round it searched everything in its reach again.
- Less stutter walking round a base: a light's shadow map that only changes size no longer searches its surroundings again, and re-ranking the lights no longer draws a shadow face in the same frame.
- New, experimental: Tiled lights (General, off by default). Every point light in one pass instead of one pass each.
- New: Timing log (General, off by default). What the mod's own work costs per frame, in the log every 5 seconds.

## 0.1.8
- Fixed: pieces round you could vanish and pop back in while the game streamed things in, when a light switched on as its piece appeared.
- The chunks a broken piece falls apart into cast moving shadows while they fall, as they do with the game's own shadows.
- Effect particles cast moving shadows too: the chips of a broken piece or tree, a stone's rock bits and the like. See-through dust doesn't.
- Leaner: shadow updates no longer make garbage for every lower-detail model in a light's reach, and leftover code from older versions is gone.
- Far less video memory: the shadow maps' storage grew to twice what it needed whenever it ran out (1.8 GB seen) and never got smaller. It now grows by what's needed and packs itself back down once lights are done updating.
- Fixed: placing a spinning wheel, refinery, windmill or ballista lagged: their moving parts made every light round them search everything in its reach again. Now they're just left out of those lights' maps.
- Maps are no longer kept for lights that unload (for coming back), which held a lot of video memory and popped in. After a portal or a world load the maps are built again over the first moments, as before, and now fade in instead of popping.

## 0.1.7
- Fixed: breaking a piece near a light left shadows of chips (wood) or rocks (stone) on the ground until you looked away. The dust, chips and rocks that fly out were drawn into the light's shadow map while it updated. The game's effects are no longer drawn into shadow maps.
- Less lag placing and breaking: each light round a change used to search everything in its reach again before updating its shadow. Now a placed piece is just added to what each light already knows, and a broken one needs nothing. The check of what's round a light is also worked out once per object instead of on every update. Lower-detail models are no longer switched off and on for every shadow update (the update always draws full detail, so they were never drawn anyway).
- Shadows change with the piece: placing or breaking something in view updates the lights you can see in the next frame, instead of one side of one light per frame after each other (it was a few frames late).

## 0.1.6
- Leaner: the mod keeps its shadow maps one way only (sized by the screen, kept as depth, both sides in one render). The two older ways it carried as fallbacks are gone (about 750 lines, a third of the lighting shader and three shaders), and so are their textures. A GPU that can't do this keeps the game's own point-light shadows.
- Coming home is smooth: shadow maps are kept when their lights unload, and a light that loads back in with nothing changed around it gets its map back without rendering. Maps that do have to be built are spread out over time.
- Less lag after a portal: the game keeps loading a base in for a few seconds after the loading screen, and every piece that appeared made the lights around it render again. A light now waits until everything around it has loaded (the same check the portal itself waits on), then renders once.
- Shadow maps render in half the work: the surfaces facing a light and the ones facing away are drawn in one pass instead of two.
- Food, meads and items on stands are no longer checked every frame.
- Fixed: torches placed by the world (the green ground torches at swamp crypts) were shaded by their own model; like built ones, they no longer are.

## 0.1.5
- Less stutter arriving at a base through a portal: shadow memory is set aside once instead of growing (and copying every shadow map) several times over while the base loads in.
- Shadow maps get smaller again as you move away from a light (they only ever grew before), so the memory follows what's on your screen.
- Fixed: shadows between you and a light further away could be low-res: a map's size was measured from the light, not from where its shadows come nearest the camera. One map texel is now no bigger than one screen pixel there.

## 0.1.4
- Follows the game's Point light shadows setting. On the highest setting nothing changes. Off: no light shadows from the mod either. Low and the default: every light within the game's own shadow distance (20 m) gets its shadows, not just 1 or 3, and moving shadows get the same work per frame the game would spend on its 1 or 3 lights, nearest lights first. Better looking than vanilla at the same cost.
- Recommended: set Point light shadows to the highest setting. That's what the mod is made for, and where it looks its best.

## 0.1.3
- Uses far less video memory (about 2 GB down to well under 1 GB on a big card). Each shadow map is sized by how much of your screen its light covers, the way games size theirs: a light far away gets a small map, one you're standing in gets the full size. Maps are kept once, as 16-bit depth, instead of twice.
- Fits your graphics card: the shadow maps take at most a fifth of its memory, and moving shadows give way first on smaller cards. A party of players near many lights could fill the memory and slow everything to a crawl.
- Faster lighting on the GPU: pixels out of a light's reach and parts of a light with nothing moving skip their shadow work.
- Moving shadows: the nearest lights get them first when there are more than fit, and their memory is freed again once things calm down.
- Lights no longer switch from a low-res to a full-size shadow map while you look at them: the best lights get full-size maps before you get close.

## 0.1.2
- Lights whose light covers more of your screen (close, or big and bright in view) get full-size shadow maps first.
- Sharper shadow edges close to a light: fine details (herbs, strings, small pots) no longer smear into blobs.
- Fixed: see-through parts of pieces (the spice rack's herbs, leaves) cast solid card-shaped shadows in cached shadows instead of their real shape.

## 0.1.1
- Faster moving shadows: only the part of a shadow map that changed is redrawn, each object is drawn once for every side of a light's map it touches, and the further out in a light's range the moving things are, the less often their shadows are redrawn (every frame close to the light, every 4th at the edge). You now cast a moving shadow out to a light's whole range.
- Placed food and meads are part of the cached shadows instead of the moving ones, and a placed mead's glow is a light of its own.
- Lights on item stands and armour stands (a trophy's flame, a weapon's glow) get shadows like any placed light, and an item no longer blocks its own light.
- Fixed: items on stands and placed items could lose parts of their cached shadow to their texture's alpha.
- Fixed: lights on items (dropped, placed or on a stand, like Brenna's trophy) cast no shadows: they kept the game's near plane, which on some is almost their whole range.
- Fixed: a mounted item's light could be treated as a rolling dropped item and keep losing its shadow map.
- Lights you walk up to switch to their full-size shadow map sooner, up to three at once.
- Benchmark: compares the full mod, every moving shadow redrawn every frame, the cached shadows alone, the cached shadows with the game's own moving shadows, and vanilla, in the same time as before.
- Coloured ambient occlusion: the game's ambient occlusion takes on the colour of the surface it lands on (GTAO multi-bounce), so bright surfaces are darkened less and coloured ones don't go grey.
- Removed: the sun shadow caster culling. Measured, it saved nothing.

## 0.1.0
- First release.
