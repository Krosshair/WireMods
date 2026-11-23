# Wire's Acoustics 0.7.0 — Window Audibility Continuity
Date: 2026-11-23
Status: unpublished release candidate; actual live-game listening and CPU audit pending.

## Reported issue
Current distributed rain sounds good but direct rain starts late near a window and continues for a short period after moving away. Direct-sector sources are rebuilt from sparse 64-room-ray visible/blocked classifications every 0.2s plus worker completion; sources fade separately over ~125ms. A minor change in listener position can cause a whole sector to drop to zero. The indirect layer was already improved and is NOT changed.

## New method
- src/RainField.cs caches up to four world-space opening positions/radii and their stable audio-sector assignment derived from the existing Room/Portals survey. No raycasts added.
- Every existing AudioMan/Ambience audio update, and only when the listener moves at least 5cm or survey geometry changes, recompute direct rain contribution from listener-to-window distance, not individual room-ray on/off results. Pure WindowAccess(r,d) = clamp(radius)/(radius + distance) times a smooth distant cutoff from 8m to 14m.
- Missed rays do not immediately delete a previously measured physical window; actual world geometry generation changes invalidate all cached opening state. This also prevents one single missed ray from turning off the entire direct source. Listener distance makes rain progressively quieter as they leave.
- World-space audio positions stay near the known outdoor opening rather than relocating to a virtual fixed 8m circle around the current listener every room scan.
- Cache supports four strongest relevant openings. A newly detected window nearer than the farthest cached candidate can take over its slot. Stable slot IDs avoid moving an active sound unnecessarily.
- Within 10m of cached opening during rain, a current sector voice may continue silently ("prewarm") to prevent a delayed new audio start when moving toward the window. Up to four voices; no separate synthetic droplets.
- Direct gain follows room/roof status independently: a large window no longer reduces its own direct rain merely because opening exposure decreases average enclosure. The existing indirect/weather layers are unchanged.
- Continuous target fade is 12 units/s, <= ~63ms for max .75 direct gain, rather than the earlier 125ms, after distance computations. The room survey still updates normal geometry at original 5Hz; continuous distance does not trigger new surveys.
- No new Update/LateUpdate/FixedUpdate, coroutines, timers, background loops, scene scans, or new raycasts. Uses existing 8 static sectors, 4 cached openings, and existing audio callback.

## Limitations
- Cached inferred aperture geometry and its centre/radius can be imprecise with only 64 probe rays. A truly new, previously unseen opening still waits for room measurement to identify it.
- Existing world-geometry invalidation responds to door/build changes; more unusual doorway geometry lacking event hooks may leave a stale remembered opening until world updates.
- Direct sound beyond ~14m from a cached window fades out even if roof/transmitted indirect rain remains audible. This is a perceptual proxy, not a full wave-propagation solver.
- A few cached warm AudioSources may consume native mixing work. Needs live-game profiler to confirm overhead.
- WS and WiresProfiler are untouched.

## Verification and release artifacts
- Production net472 build zero warnings/errors.
- Math/source-linked window tests: exact on-window gain, monotonic decrease at 5cm spacing, smooth 8m–14m fade, stale geometry invalidation, aperture ranking, same-source warm start, 0 extra ray jobs.
- Original rain field, continuous rain, rain doorway and full regression suite: PASSED across 52 production C# files. 0 errors / 0 warnings. Report RELEASE_CANDIDATE_VERIFICATION_20261122.txt.
- Verified DLL SHA256 27B74D2E140D2160B9CFA5D8220DA1BB392B3890E8530E1FA839F4A21B8366D0. Installed into Enngard and package.
- Staged ZIP staging/WiresAcoustics-0.7.0-RC-window-continuity-20261123.zip, SHA256 E90F6BE07DC24B55BE8922D7171E08E9038650A3C1C6F6205504829FAAFB1F1A. Five required root entries verified. No publication.
- Live in-game sound and CPU NOT VERIFIED.
- Backup: backup/RAIN_WINDOW_CONTINUITY_20261123/Restore.ps1. Requires Valheim closed.
- No public release or tag.
