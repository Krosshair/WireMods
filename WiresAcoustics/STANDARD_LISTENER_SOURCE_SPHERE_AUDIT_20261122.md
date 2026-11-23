# Wire's Acoustics 0.7.0 - Documented Listener and Source-Sphere Design
Local engineering development, 2026-11-22. NOT public release.

## Source standards used (and not claimed beyond their actual scope)
Wwise Unity "AkGameObj3rdPersonCam": third-person listener position can
follow the player avatar while audio orientation follows the camera.
https://documentation.help/Wwise-Unity-Integration/pg__appscripts.html
Steam Audio Source: Volumetric occlusion casts from the listener to points
distributed in a sphere around the source, controlled by Occlusion Radius
and Occlusion Samples. This is a source radius authoring parameter.
https://valvesoftware.github.io/steam-audio/doc/unity/source.html
Steam Audio C API: volumetric occlusion is the fraction of sampled
source points which are unoccluded; geometric transmission is a distinct
direct-sound effect.
https://valvesoftware.github.io/steam-audio/doc/capi/simulation.html

## Removed unsupported camera/character split
Previously WA computed wall occlusion, diffraction, corners and portals
from Hearing.Eye (the camera position), but measured physical distance
and Doppler from Hearing.At (the game AudioListener transform). Moving the
third-person camera could change sound blockage without moving the avatar.
Now the same physical listener position Hearing.At drives all of them.
The old second raycast-origin field Hearing.Eye was removed, not aliased.
Hearing.Camera still supplies camera orientation to Directions/HRTF.
This chooses the documented Wwise third-person position/orientation
configuration; a camera-based listener is also a valid DESIGN choice
in a different configuration, but no longer mixed implicitly.

## Removed unreferenced custom volume shape
Previous SourceVolume sampled an object's collider BOX using Halton
points, and an arbitrary fixed sphere for unshaped sources.
This was not Steam Audio's documented source-volume geometry.
SourceVolume now generates deterministic low-discrepancy points uniformly
inside the configured 3D emitting SPHERE.
For measured looping sources, the sphere is centered at the measured
owner bounds center. Its radius encloses the 3D scaled/rotated collider
half-axes (sqrt(ax²+ay²+az²)); this is an explicit conservative
geometry approximation, not a model of the actual burning flame mesh.
For unshaped sounds or one-shots, the sphere is centered at the
authored sound position. Its radius is an exposed BepInEx tuning parameter:
[Tuning] Unshaped source sphere radius = 0.30 metres by default, range
0.05..3 metres. 0.30m is a gameplay authoring default, NOT a universal
physical/acoustic constant or a value mandated by Steam Audio.
Ray count is user-selectable (0/4/8/16/32 cap) with an adaptive
apparent-radius policy; the heuristic thresholds only control the
quality/performance budget, not the acoustic attenuation law.

## Existing physical propagation retained
- Spectral transmission is still derived from the Unity collider
  materials and validated two-pole DSP band-energy fitting.
- Single-edge Fresnel is evaluated per source-volume sample for
  validated single-edge geometry.
- In-place source-volume changes do not add frame hooks/coroutines.
- Geometry is still physics-ray-based, portal/corner reconstructions
  are approximations and not identical to Wwise authored portals or
  Steam Audio native scene simulation.

## Verification
- Production net472 Release builds 0 errors/warnings.
- Production Hearing.cs and SourceVolume.cs compiled into synthetic test
  harness: third-person camera orientation remains distinct while
  listener position follows the avatar transform.
- 766 deterministic source-sphere geometry / radius / emitter rotation /
  scale / quality checks passed.
- 1604 analytic source volume Fresnel field checks passed.
- 510 ray buffer layout combinations passed.
- Full existing system regressions (DSP 354, geometry 324, corner 400,
  obstacle 75, acoustics trace, mixed paths, invalidation, concurrent
  100k packet meter) all passed on the source snapshot.
- Tests check source files for ANY remaining Hearing.Eye call sites,
  preserve Hearing.Camera for spatialization, and detect newly
  introduced per-frame or coroutine hooks.
- No Valheim gameplay listening or profiler benchmark completed.
  No CPU/fps or full-engine acoustic parity claimed.

## Material limits that must remain visible
A sphere enclosing a fire collider can cover space that is not burning,
especially for elongated objects. An authored per-fire sound-emission
sphere would be more precise. Do not claim the collider sphere measures
the exact visible flame silhouette. Uniform ray samples may still show
coverage quantization; greater sample quality has physics CPU cost.
A 125 Hz/DC plus 2 kHz band representation is not full-band spectral
transmission. No SOFA-based HRTF validation or true room IR convolution.
No perfect final-game auditory outcome can be inferred from unit tests.

## Files
dev/TestStandardListenerSplit.ps1
dev/VolumeSamplingTests/Program.cs
dev/VerifyEveryImplementation.ps1
dev/VerifyReleaseCandidate.ps1
backup/STANDARD_LISTENER_SPHERE_20261122/Restore.ps1

## Approval
Private staged development build only; no Thunderstore publication.
Rollback requires Valheim closed.
