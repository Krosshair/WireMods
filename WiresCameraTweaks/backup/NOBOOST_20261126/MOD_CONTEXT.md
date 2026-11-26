# Wire's Camera Tweaks: context

GUID wire.valheim.shakeitup (kept from Shake It Up for config carry-over). Namespace WiresShakeItUp, csproj WiresCameraOverhaul.csproj, DLL WiresCameraTweaks.dll.

## Parts
- ShakeEngine.cs: camera shake rework. CameraSystems.cs: smooth zoom + collision auto-zoom.
- LensEffect.cs: image effect added to the main camera in CameraEffects.Awake. Replaces Valheim's DoF (Standard Assets DepthOfField, m_dof) while the in-game DoF setting is on (SetDof postfix keeps m_dof off).
- DepthOfFieldEngine.cs: fallback if the shader bundle fails: Valheim's DoF with smoothed log-space focus.

## Lens shader
- Source: `C:\Users\kajet\LightRework\Assets\Shaders\WireLens.shader` (Unity 6000.0.75f1). He builds via "Wire > Build Camera Tweaks bundle" -> `build\wirecameratweaks`; `InstallLens.cmd` embeds it as `src\wirecameratweaks.bundle`, builds, installs.
- Modelled on Teardown's shaders (read from teardown.exe 2026-11-26): focus = diopter average in golden-angle discs (5% and 15% of screen) around centre, near/far planes = the two averages; CoC in diopters (near: 1/focusNear - 1/d, far: 1/focusFar - 1/d). Blur gather = Gustafsson single-pass bokeh at half res.
- Changes vs Teardown: depth closer than the player (center distance + 0.49 m capsule) is ignored for focus, near blur only in front of the player, real-time smoothing (0.22 s both ways = Teardown 0.2/frame at 60 fps), far plane capped at Valheim's ship min (200 m) while steering/building.
- Values: Aperture 30 mm = 50 mm f/1.8 (he found 8 mm, a human pupil, barely visible) (physical CoC: A / (2 tan(fov/2)) per diopter). Max blur 40 px at 1080p (2x Gustafsson). Bokeh: tonemapped highlights (lum>0.7) lifted up to 5x where blurred; threshold and gain are picks. Lift turns off when a 6x6 grid of the frame shows >1 (HDR, e.g. RenoDX: its uber shader tonemaps before our pass, unclamped; held ~30 s).

## Next
- Helldivers 2 DoF unknown (packed shaders).
- Barrel distortion tried, removed: doesn't fit Valheim.
