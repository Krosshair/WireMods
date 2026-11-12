# Wire's Shadows

Real shadows for every light you place, without the performance cost.

In vanilla Valheim only the few lights nearest you get shadows, and the game redraws those every frame. Every other sconce and torch shines straight through walls, floors and roofs. Wire's Shadows gives every placed light cached shadows instead and makes the sun's shadows cheaper, so your base looks right and runs about as fast as vanilla, or faster.

## Features

* **Cached shadows for every placed light:** sconces, torches, braziers, hearths, campfires, lanterns, forges and the rest. Each light's shadow map is rendered once and only re-rendered when something near it changes (building, removing, a door opening or closing).
* **No light leaking** through walls, floors or roofs.
* **Dynamic shadows** for things that move: players, creatures, doors and gates, dropped items, item stands, and the moving parts of windmills, spinning wheels, the refinery and turrets.
* **Cheaper sun shadows:** pieces the sun can't reach (under a roof, behind a wall, in a basement) are culled from the sun's shadow map. Nothing you see changes. It follows the sun as it moves and updates as soon as you build, remove or open something.
* **The vanilla look:** Valheim's own lighting shader, rebuilt with the shadows added. Same banding, same fades, same flicker.
* Carried torches keep the game's own shadows.

## Limitations

* **No fire wiggle.** In vanilla a fire's light shakes around a little. A cached shadow is rendered from one spot, so here the light holds still. Its brightness still flickers.
* **No swaying shadows.** Banners, hanging cloth and herbs cast their shadow at rest, not moving with the wind.

## Settings

* **Enabled:** off brings back the game's own lighting and shadows.
* **Debug log:** off by default. Writes what the mod does to the log; turns itself on while a benchmark runs.
* **Benchmark:** measures the mod against vanilla where you stand (the full mod, the mod without its dynamic shadows, the shader alone, and vanilla) and shows the frame times.

## Performance

The goal: run the same as vanilla with point lights maxed out, or slightly better, while looking much better.

On my PC (RTX 4090, i5-13600KF, 128 GB RAM), in my base of about 6,000 pieces, one benchmark gave 17.40 ms a frame (57 fps) with the mod and 17.73 ms (56 fps) in vanilla. Other runs were up to 2.5 ms faster than vanilla. It depends on what's happening: it's faster nearly every time, but I can't guarantee it always is.

Measure it yourself: turn on Benchmark in an in-game config menu (such as Shudnal's Configuration Manager).

## Compatibility

* Client side only.
* Windows (DirectX 11). Where the shaders can't load, the game's own lighting stays.
* Works with Valheim Performance Optimizations.
* Mods that merge building pieces into bigger meshes: their meshes are left as they are.
* Mods that replace the game's lighting shader can't be used together with this one.

Disclaimer: development is AI assisted. Not fully human or AI.

Donations: [ko-fi.com/krosshair](https://ko-fi.com/krosshair)
