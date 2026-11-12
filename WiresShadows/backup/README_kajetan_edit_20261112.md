# Wire's Shadows

In vanilla Valheim most lights cast no shadow at all, so a sconce or a torch lights the next room straight through the wall, the floor above and the roof. The few lights that do get shadows are the nearest handful, and the game draws those again every frame.

## What it does

* **Cached point light shadows for every placed light:** sconces, torches, braziers, hearths, campfires, lanterns, forges and the rest. Each light's shadow map (a cube map) is rendered once and cached, and only re-rendered where something near it changes: a piece built or removed, a door opening or closing. Vanilla renders its few shadowed lights again every frame.
* **No light through walls.** Floors, roofs and walls block light the way they look like they should.
* **Dynamic shadows** for what moves: you, other players and creatures, swinging doors and gates, dropped items, item stands and the moving parts of machines (windmills, spinning wheels, the refinery, turrets). They go into separate shadow maps that update while they move, and back into the cached ones when they stop.
* **The game's own look:** Valheim's lighting shader, rebuilt with the shadows added. Same banding, same fades, same fire flicker in brightness.
* **Cheaper sun shadows (shadow caster culling).** The sun's shadow map only keeps the nearest caster, so a piece the sun can't reach (under a roof, behind a wall, in a basement) can't change it. Those pieces are culled as sun shadow casters, and what you see stays the same. It follows the sun as it moves, and anything you build, remove or open is taken into account at once.
* Carried torches keep the game's own shadows.

## Limitations

* No fire wiggle: in vanilla a fire's light shakes around a little. A cached shadow is rendered from one spot, so here the light holds still (its brightness still flickers).
* Cached shadows don't sway: things that move in the wind (banners, hanging cloth, herbs) cast their shadow at rest.

## Settings

* **Enabled:** off brings back the game's own lighting and shadows.
* **Debug log:** off by default. Writes what the mod does to the log; on by itself while a benchmark runs.
* **Benchmark:** measures the mod against vanilla where you stand (the full mod, the mod without its moving shadows, the shader alone, and vanilla) and shows the frame times.

## Performance

On my PC (RTX 4090, i5-13600KF, 128 GB RAM) the mod runs about the same as vanilla: sometimes better, possibly (hopefully not) worse. The goal is to run the same as vanilla with point lights maxed out, or slightly better, with much better visuals.

One benchmark in my base (about 6,000 pieces), with the mod's built-in benchmark: the mod at 17.40 ms a frame (57 fps), vanilla at 17.73 ms (56 fps). I've had benchmarks have the difference be even 2.5ms+ faster than vanilla (it completely depends on what's happening, it Will be faster 99-100% of the time but I can't absolutely guarantee perfection).

You can measure it yourself with the Benchmark setting, from an in-game config menu (such as Shudnal's Configuration Manager).

## Compatibility

* Client side only.
* Windows (DirectX 11). Where the shaders can't load, the game's own lighting stays.
* Works with Valheim Performance Optimizations.
* Mods that merge building pieces into bigger meshes: their meshes are left as they are.
* Mods that replace the game's lighting shader can't be used together with this one.

Disclaimer: development is AI assisted. Not fully human or AI.

Donations: [ko-fi.com/krosshair](https://ko-fi.com/krosshair)

