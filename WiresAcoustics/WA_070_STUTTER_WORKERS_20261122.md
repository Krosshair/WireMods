# WA 0.7.0 - stutter, nonblocking jobs and analytical worker pass
Date: 2026-11-22
Status: installed in Enngard **development only**, NOT publicly released. Runtime audio/performance acceptance remains OPEN.

## Starting evidence
- User reports occasional in-game stutter and roughly 0.7 ms WA profiler main-thread CPU.
- Prior independent audit found Unity Heightmap.GetHeight ArgumentOutOfRangeException inside Occlusion.Ridge -> Batch.Ray -> AudioMan.Update.
- Prior debug report included 515.079 ms and 193.504 ms room-tick maxima; log-output overhead was included, so those values were NOT demonstrated debug-disabled WA algorithm costs.
- Previous debug trace was overwriting ~68% of history and selecting muted/virtual sources.
- Batch already used asynchronous RaycastCommand jobs and frame-bounded Apply; Directions had an unconditional TransformAccess job Complete, and Room.Run had an unconditional room-ray job Complete.

## Changes installed
1. Room.cs: only Complete an already finished room-ray job. If pending, leave last stable room acoustic state; recheck in existing AudioMan.Update hook.
2. Directions.cs: only Complete an already finished transform-access job, never block the normal main-thread audio update while it runs. Existing tracking and event-driven behavior unchanged.
3. Occlusion.cs: handle ArgumentOutOfRangeException from Heightmap.GetHeight during 15-sample ridge scan; keep original soft-terrain estimate on missing terrain rather than abort AudioMan.Update.
4. AnalyticWorker.cs (new): collect main-thread snapshots of BoxCollider transforms, source volume, wall coefficients, listener/source positions; perform pure disk-rectangle coverage and Fresnel diffraction calculations with one pooled ThreadPool worker item per source batch. Unity objects/audio source/filter writes remain strictly main thread. Capture/compute/publish use preallocated arrays and a 0/1/2 volatile handoff. No per-source tasks, no extra Update/FixedUpdate/LateUpdate/coroutine/polling.
5. Directions.cs: main-thread ApplyReady validates source/wall/world generation and uses SpectralPaths.Decode/filter.Set. Worker output is typically one update later. Worker exceptions fall back to old synchronous solver for the affected source.
6. AcousticTrace.cs: longer 512-entry ring; top-24 strongest retained gain/cutoff transitions rather than 128 synchronous log lines; at most 12 recent geometry lines rather than 64; muted and virtual sources no longer occupy top-source slots.
7. Enngard wire.valheim.acoustics.cfg Debug log changed from true to false for clean profiling; config backed up. Travel time remains true.

## Numerical / static verification (not in-game)
- Production net472 build: success with 0 errors and 0 warnings.
- Independent worker-vs-original single-wall numerical comparison: PASS, 360 randomized position/rotation/size/material/strength configurations; matching coverage, gain, cutoff, bend share and apparent position.
- AnalyticOcclusionTests: 10364 checks PASS (source-linked; includes worker cases).
- AcousticTraceTests: PASS.
- Full dev/VerifyEveryImplementation.ps1: PASS.
- Updated dev/VerifyReleaseCandidate.ps1 (50 production CS files; worker contract): PASS. Report RELEASE_CANDIDATE_VERIFICATION_20261122.txt.
- No in-game stutter profile, perceptual validation or new runtime log yet, so **do not claim the stutter is resolved**.
- Multiwall/terrain/portal continuous solver and reported gain-target switches have NOT been fully redesigned during this patch.
- Unit tests were not an end-to-end Unity source movement/load/unload/destroy test.

## Important performance architecture constraint
- Unity Collider/Transform/AudioSource/MonoBehaviour state still only read or written on main thread.
- Physics uses Unity RaycastCommand jobs, directions use TransformAccess job, pure analytical single-wall calculations now use a pooled worker.
- Batch.Apply remains main thread because it resolves actual Unity colliders and changes live AcousticFilter state. The audio callback must fill its buffer synchronously; routing the callback itself to ThreadPool would create audio underruns.
- Offloading is not itself proof of lower total CPU; compare main-thread p50/p95/p99, worker thread CPU, audio thread CPU, and p99 frame times.

## Next in-game test (debug OFF)
1. Enngard mod loaded; check that sound sources follow their actual emitter as before.
2. Walk slowly near a smelter's wall and around two real walls; watch for late update/jumps and sharp cutoff changes.
3. Test rain under a roof, partially open building, cave and terrain ridge.
4. Compare Unity profiler main thread / job system / audio thread versus the prior ~0.7 ms in comparable scenes; inspect spikes, not only average.
5. Toggle WA off/on once to exercise worker Flush and restoration.
6. If detailed trace needed, toggle Debug log on *only for short troubleshooting*, then turn it off for timing measurements.

## Installation / restore
Installed:
C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll
SHA256:
FD97C28D9CC867390A8848AAA54D52D7FAAA5DEE0CCDAF5420C6ADDB031FAA8F

Backup (source, config, installed DLL, changed test scripts):
backup\STUTTER_THREAD_JOBS_20261122
Rollback with Valheim CLOSED:
powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\kajet\WireMods\WiresAcoustics\backup\STUTTER_THREAD_JOBS_20261122\Restore.ps1

The original package/WiresAcoustics.dll and stage_next/WiresAcoustics.dll were NOT modified; no release or publish occurred.
