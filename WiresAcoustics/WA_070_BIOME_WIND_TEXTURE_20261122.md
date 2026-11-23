# Wire's Acoustics 0.7.0 — biome wind-texture directional/shelter correction
Date: 2026-11-22
Status: source fix built, test suite passed, installed to Enngard and staged as new release candidate. Runtime listening/rain/performance OPEN, not published.

## Evidence from the actual extracted game
ValheimSOURCE/ValheimProject/ExportedProject/Assets/Systems/_Environment.prefab
- Environment 'Heath clear' (used by Plains) m_ambientLoop GUID 1235e2bef68be7f488efa67b11f916db.
- The GUID corresponds to Audio/Ambients/loops/SW008_Wendland_Autumn_Wind_In_Reeds_Medium_Distance_Leaves_Only.ogg.
- Other clear presets reference the same authored wind-in-reeds recording; this is NOT unique to Plains.
ValheimSOURCE/csharp/assembly_valheim/AudioMan.cs
- m_ambientLoopSource created spatialBlend=0, separate from 2D m_windLoopSource.
- Existing WA Ambience.Pan applied wind-direction/gust only to m_windLoopSource.
- Biome m_ambientLoopSource was a diffuse 2D bed with only enclosure-dependent filtering, explaining lack of direction and upwind shelter in the Plains wind recording.

## Targeted correction
- Ambience.IsWindTexture identifies wind-texture recordings by a 'Wind_' token in their actual clip name. Tracks clip identity and caches classification to avoid repeated AudioClip.name native/managed property lookup in Pan/Glide.
- The existing Ambience.Pan step applies a broad HRTF windward bearing when enabled, or controlled stereo panning with HRTF off, to wind-texture ambient loops. Bearing from EnvMan.GetWindDir and existing Room.Exposure upwind route. Sounds remain diffuse/wide rather than fake point emitters.
- The existing s_windModulation gust/directional amplitude also applies to wind-texture loops. Existing s_exposure gliding applies up to 12 dB windward shelter reduction. Existing 2D clip playback and authored overall volume remain intact.
- Ambience.Glide also applies the wind loop's upwind low-pass response to wind-texture ambience.
- Non-wind ambience, rain and bird recordings are excluded and reset on clip switches; rain roof response and opening-position copies remain as before.
- Wind direction works even if Muffling is disabled; wind specific occlusion only uses the exposure/strength when enabled. No new physics scans, GameObject clones, sample mutation, per-frame MonoBehaviour hook, coroutine or polling.
- Previous loop-start/stop fade experiment remains removed and the prior 0.7.0 cleanup is retained.

## Verification
- dev/TestAmbientWindTexture.ps1 extracts production wind classifier, tests wind-vs-rain/bird names and the real Heath clear asset GUID, validates source integration and caching.
- net472 production build: 0 errors, 0 warnings. Full VerifyReleaseCandidate: PASS (50 production C# files); full VerifyEveryImplementation: PASS.
- Development DLL deployed to Enngard and matching package, SHA256 1ECFE1BA9CC9DCCE6D829127979EA28A279714FCCEA624D21608296EDB408740.
- New staged ZIP: staging/WiresAcoustics-0.7.0-RC-wind-texture-rain-pending.zip, SHA256 52D586784F7F07DD77A5262B08A1F98A428DF194565D09448A857F15B2177E92. Previous RC retained separately.
- Real-time in-game direction, shelter, clipping/music and CPU load NOT yet measured on this build.
- No public release has been made.

## Backup and rollback
backup/AMBIENT_WIND_TEXTURE_20261122 holds exact pre-patch source, release scripts, package and Enngard installed DLL.
backup/AMBIENT_WIND_TEXTURE_20261122/Restore.ps1 restores these with Valheim closed.
