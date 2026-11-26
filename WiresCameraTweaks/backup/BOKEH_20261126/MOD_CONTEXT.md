# Wire's Camera Tweaks: context

GUID wire.valheim.shakeitup (kept from Shake It Up for config carry-over). Namespace WiresShakeItUp, csproj WiresCameraOverhaul.csproj, DLL WiresCameraTweaks.dll.

## Parts
- ShakeEngine.cs: camera shake rework. CameraSystems.cs: smooth zoom + collision auto-zoom.
- LensEffect.cs: image effect added to the main camera in CameraEffects.Awake. Replaces Valheim's DoF (Standard Assets DepthOfField, m_dof) while the in-game DoF setting is on (SetDof postfix keeps m_dof off), plus barrel distortion.
- DepthOfFieldEngine.cs: fallback if the shader bundle fails: Valheim's DoF with smoothed log-space focus.

## Lens shader
- Source: `C:\Users\kajet\LightRework\Assets\Shaders\WireLens.shader` (Unity 6000.0.75f1). He builds via "Wire > Build Camera Tweaks bundle" -> `build\wirecameratweaks`; `InstallLens.cmd` embeds it as `src\wirecameratweaks.bundle`, builds, installs.
- Modelled on Teardown's shaders (read from teardown.exe 2026-11-26): focus = diopter average in golden-angle discs (5% and 15% of screen) around centre, near/far planes = the two averages; CoC in diopters (near: 1/focusNear - 1/d, far: 1/focusFar - 1/d); barrel r*(0.94+0.04r^2). Blur gather = Gustafsson single-pass bokeh at half res.
- Changes vs Teardown: depth closer than the player (center distance + 0.49 m capsule) is ignored for focus, near blur only in front of the player, real-time asymmetric smoothing (Blur In 1 s, Clear 0.35 s), far plane capped at Valheim's ship min (200 m) while steering/building.
- Values: Aperture 8 mm = dark-adapted human pupil (physical CoC: A / (2 tan(fov/2)) per diopter). Max blur 20 px at 1080p (Gustafsson). Blur In/Clear times are my picks, not derived.

## Next
- Helldivers 2 DoF unknown (packed shaders).
- Offered, not decided: Teardown's vignette (20% at edges, in the same pass as barrel).
