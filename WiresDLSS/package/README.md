# Wire's DLSS

A small native NVIDIA DLSS/DLAA integration for Valheim with a single mode setting in **Settings → Graphics**.
This only exists because the other mods that added DLAA are incompetent bloatware. This does NOT add FG (use Smooth Motion for that btw, I noticed Smooth Motion looks better than other mod's nVidia framegen). The others also don't support RenoDX, which i use. You can fully use reshade with this mod. Also no FSR or anything of the sort, ONLY dlss and dlaa.

## Options

- **Off** — Valheim's normal rendering
- **DLAA** — native-resolution neural anti-aliasing, no sharpening
- **DLSS Quality** — ~67% internal resolution
- **DLSS Balanced** — ~58% internal resolution
- **DLSS Performance** — ~50% internal resolution

The mod preserves the game's HDR/tonemapping pipeline. It doesn't include a separate menu, shader packs, sharpening, frame generation, DXGI replacements, or a frame limiter.

### Installing

Install the package with your mod manager. The first launch copies **only** `NVUnityPlugin.dll` and `nvngx_dlss.dll` from the mod folder into the Valheim root, where Unity requires the native NVIDIA plugin at startup. **Restart Valheim once** to activate DLAA/DLSS. NO OTHER FILES ARE ADDED OR CHANGED! just what is needed.

A native NVIDIA GPU/driver (obviously lol) and Direct3D11/12 are required. If NVIDIA's backend is unavailable, the mod keeps the vanilla image and logs the reason.
