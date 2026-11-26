# Wire's Camera Tweaks: context

GUID wire.valheim.shakeitup (kept from Shake It Up for config carry-over). Namespace WiresShakeItUp, csproj WiresCameraOverhaul.csproj, DLL WiresCameraTweaks.dll.

## Parts
- ShakeEngine.cs: camera shake rework. CameraSystems.cs: smooth zoom + collision auto-zoom.
- LensEffect.cs: image effect (Quality LensBlur; Vanilla = m_dof + DepthOfFieldPatch) added to the main camera in CameraEffects.Awake. Replaces Valheim's DoF (Standard Assets DepthOfField, m_dof) while the in-game DoF setting is on (SetDof postfix keeps m_dof off).
- DepthOfFieldEngine.cs: fallback if the shader bundle fails: Valheim's DoF with smoothed log-space focus.

## Lens shader
- Source: `C:\Users\kajet\LightRework\Assets\Shaders\WireLens.shader` (Unity 6000.0.75f1). He builds via "Wire > Build Camera Tweaks bundle" -> `build\wirecameratweaks`; `InstallLens.cmd` embeds it as `src\wirecameratweaks.bundle`, builds, installs.
- Modelled on Teardown's shaders (read from teardown.exe 2026-11-26): focus = diopter average in golden-angle discs (5% and 15% of screen) around centre, near/far planes = the two averages; CoC in diopters (near: 1/focusNear - 1/d, far: 1/focusFar - 1/d). Blur = Jimenez CoD:AW via Unity PPv2 DoF: half-res prefilter (4-tap, strongest CoC, focused faded), 6-ring kernel 148 taps with bg/fg split, tent postfilter, full-res combine.
- Changes vs Teardown: depth closer than the player (center distance + 0.49 m capsule) is ignored for focus, near blur only in front of the player, real-time smoothing (0.22 s both ways = Teardown 0.2/frame at 60 fps), far plane capped at Valheim's ship min (200 m) while steering/building.
- Values: Aperture 30 mm = 50 mm f/1.8 (he found 8 mm, a human pupil, barely visible) (physical CoC: A / (2 tan(fov/2)) per diopter). Max blur 26 px (PPv2: 4 px per ring past the first + 6, screen px, max 5% height). Earlier Gustafsson gather with coarser step showed dots. No highlight boost: Valheim's image (HDR under RenoDX) is blurred as it is, his call.

## Next
- Helldivers 2 DoF unknown (packed shaders).
- Barrel distortion tried, removed: doesn't fit Valheim.
