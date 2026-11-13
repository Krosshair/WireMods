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
