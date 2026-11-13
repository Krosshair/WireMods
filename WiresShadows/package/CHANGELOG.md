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
