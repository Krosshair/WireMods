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

- DynamicCamera.cs (0.2.1): one GameCamera.UpdateCamera postfix sets FOV (m_fov + dynamic + Wide Lens) and position offsets; rotation untouched. Derived: fall FOV and landing dip use downward speed (feather cape!), ramping over the free-fall speeds of Valheim's fall-damage drops 4-20 m under gravity 20 (12.65-28.28 m/s; dip from 0.8 m = 5.66 m/s); follow uses full 3D velocity; speed FOV normalized by player m_speed 4 / m_runSpeed 7. Picks: sprint +5 (cap 2x), fall +8, tired -3 below 25% stamina or encumbered, FOV settle 0.35 s, dip 0.25 m spring omega 12, follow lag 0.15 s lead 0.06 s max 0.5 m with SphereCast against m_blockCameraMask.

## Next
- Helldivers 2 DoF unknown (packed shaders).
- Barrel distortion back as opt-in Wide Lens (0.2.1, WideLens.cs + WireLens pass 5): Teardown curve, amount slider, plus FOV added in a GameCamera.UpdateCamera postfix (both cameras). Off by default.
- FFT convolution bloom tried 2026-11-27, dropped: Spencer PSF looked wrong (glints too hard, fire too weak), matching vanilla's level layout just looked like vanilla. Code in backup/BLOOM03_ABANDONED_20261127 and _removed/FftBloom_20261127.cs.
- Menu/aim DoF states (0.2.1) tried 2026-11-27, dropped. Code in backup/DOFSTATES_ABANDONED_20261127.
