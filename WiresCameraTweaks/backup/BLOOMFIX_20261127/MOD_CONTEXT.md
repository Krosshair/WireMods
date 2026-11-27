# Wire's Camera Tweaks: context

GUID wire.valheim.shakeitup (kept from Shake It Up for config carry-over). Namespace WiresShakeItUp, csproj WiresCameraOverhaul.csproj, DLL WiresCameraTweaks.dll.

## Parts
- ShakeEngine.cs: camera shake rework. CameraSystems.cs: smooth zoom + collision auto-zoom.
- LensEffect.cs: image effect (Quality LensBlur; Vanilla = m_dof + DepthOfFieldPatch) added to the main camera in CameraEffects.Awake. Replaces Valheim's DoF (Standard Assets DepthOfField, m_dof) while the in-game DoF setting is on (SetDof postfix keeps m_dof off).
- DepthOfFieldEngine.cs: fallback if the shader bundle fails: Valheim's DoF with smoothed log-space focus.

## Lens shader
- Source: `C:\Users\kajet\LightRework\Assets\Shaders\WireLens.shader` (Unity 6000.0.75f1). He builds via "Wire > Build Camera Tweaks bundle" -> `build\wirecameratweaks`; `InstallLens.cmd` embeds it as `src\wirecameratweaks.bundle`, builds, installs.
- Modelled on Teardown's shaders (read from teardown.exe 2026-11-26): focus = diopter average in golden-angle discs (5% and 15% of screen) around centre, near/far planes = the two averages; CoC in diopters (near: 1/focusNear - 1/d, far: 1/focusFar - 1/d). Blur = Jimenez CoD:AW via Unity PPv2 DoF: half-res prefilter (4-tap, strongest CoC, focused faded), 6-ring kernel 148 taps with bg/fg split, tent postfilter, full-res combine.
- Changes vs Teardown: depth closer than the player (center distance + 0.49 m capsule) is ignored for focus, near blur only in front of the player, real-time smoothing (0.22 s both ways = Teardown 0.2/frame at 60 fps), far plane capped at Valheim's ship min (200 m) while steering a ship (vanilla).
- Values: Aperture 30 mm = 50 mm f/1.8 (he found 8 mm, a human pupil, barely visible) (physical CoC: A / (2 tan(fov/2)) per diopter). Max blur 26 px (PPv2: 4 px per ring past the first + 6, screen px, max 5% height). Earlier Gustafsson gather with coarser step showed dots. No highlight boost: Valheim's image (HDR under RenoDX) is blurred as it is, his call.

## Next
- Helldivers 2 DoF unknown (packed shaders).
- Barrel distortion tried, removed: doesn't fit Valheim.

## Bloom (0.3.0)
- FftBloom.cs: prefix on PPv1 BloomComponent.Prepare. Keeps the uber shader variant and keywords (BLOOM / BLOOM_LENS_DIRT) so RenoDX's CRC-matched uber replacement still applies; only _BloomTex and _Bloom_Settings (1,1) change. Dirt intensity = dirt * vanilla bloom intensity (0.3) to keep vanilla's dirt:bloom ratio.
- WireBloom.compute (same bundle): scene halved down to fit half of a 2048x1024 float buffer (black padding = no wrap), packed (R+iG, B), radix-2 FFT in groupshared (32 KB), multiply by kernel spectrum normalized by its DC term, inverse. Kernel rebuilt only when degrees per texel change.
- Kernel: Spencer et al. 1995 photopic PSF minus its core: 0.478*20.91/(t+0.02)^3 + 0.138*72.37/(t+0.02)^2, t in degrees. Centre texel 0 (that's the image). No threshold (physical).
- Strength 0.1 = share of light in the glow: my pick. Spencer's eye scatters ~0.3. Vanilla ingame.asset: bloom 0.3, threshold 0.7, dirt 10.4.
