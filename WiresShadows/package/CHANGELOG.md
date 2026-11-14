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
