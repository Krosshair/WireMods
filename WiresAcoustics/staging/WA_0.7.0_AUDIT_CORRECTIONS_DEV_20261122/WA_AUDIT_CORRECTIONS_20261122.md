# Wire's Acoustics 0.7.0 — source-audit corrections
Private development candidate, 2026-11-22

## Scope
Actual production changes after the 421+4 function/source audit. Files:
Ambience.cs, Filter.cs, Budget.cs, Occlusion.cs, Batch.cs,
Room.cs, Ocean.cs, GeometryEvents.cs.

## 1. Wind gain (source-confirmed update-order defect)
ValheimSOURCE AudioMan.Update (render loop) and AudioMan.FixedUpdate
(physics loop) are separate. AudioMan.UpdateWindAmbience writes
m_windLoopSource.volume in FixedUpdate. Prior WA Ambience.Pan multiplied
that same field during render Update, potentially several times per
FixedUpdate, and repeated during pauses.
Correction: leave original AudioSource.volume entirely vanilla-owned.
Calculate gust/directivity in dB, then assign a noncumulative linear
gain to WA AcousticFilter.SourceLevel; existing audio-thread smoothing
handles it. Wind copies receive the same gain via their own filters.
SourceLevel >1 is now included in NeedsWork; on reset gain returns to 1.
No new MonoBehaviour Update/FixedUpdate or coroutine introduced.
Original wind gust response and directional authored curves retained.

## 2. No arbitrary -60dB source kill based on estimated acoustic gain
Budget.Step no longer mutes or demotes a playing source when f.Heard
estimates less than 0.001 (-60 dB). It releases any existing mute and
allows normal material/distance DSP to attenuate continuously.
This eliminates a design-level threshold implicated in reports of
sudden absolute silence behind walls. Unity's own voice allocation still
applies. The preexisting extremely-low (< -80dB) audio-buffer fast
zeroing remains for CPU; it is a separate limit and must be evaluated
if extremely loud upstream source volumes make such a signal audible.

## 3. No indefinite queued raycast starvation
Batch.Step previously returned before starting any raycast work whenever
Frame.Over (0.5ms deadline) was already true. If required systems
consumed that budget every frame, no new requests could start.
It now accepts the first queued request even when over budget and
defers further requests through the existing m>0 && Frame.Over guard.
This prevents budget-only starvation once earlier in-flight jobs have completed; it is not a guarantee of a request on every frame or a 0.5ms worst-frame cost.
Actual frame-cost and late-query response still need profiler data.

## 4. Remove 2s stationary source periodic raycasts
Occlusion.Unchanged now depends on motion and nearby known geometry
changes only, not the previous 2.0 s timeout. WatchLoop continues to
run in the established direction update scheduler without new hooks.
A fallback for unreported third-party dynamic colliders is no longer
present; mods must signal invalidation or source movement.

## 5. Stationary room and shoreline scans
Room.Run no longer forces 192 room rays each second for a completely
stationary listener and unchanged known world geometry.
Ocean.Postfix no longer performs 96 shoreline casts every 4 seconds
when the listener has not moved. Shoreline invalidates on terrain edits
or global zone reloads (GeometryEvents.Apply 'everywhere' only).
Opening a door or placing an unrelated object no longer invalidates
the ocean; Room still invalidates normally.
Ocean's 4 Hz shelter update and all normal camera/head tracking remain.
Unreported third-party world changes can now leave the cached scene
outdated until movement or a recognized event.

## Not claimed
- No game runtime audio listening, profiler p50/p95/p99, or pause
  transition test. All automated checks are offline.
- Not a complete event-only overhaul: existing per-frame direction job,
  half-second active-source scan, periodical sound-enabling and 4 Hz
  ocean shelter model remain in the baseline architecture.
- No replacement for WA's two-band source spectrum, handmade HRTF
  model, finite visibility sampling or room first-hit limitation.
- No absolute "no downsides" claim: reliable progress past deadline
  can cause bounded spikes; replacing safety polling with world events
  can miss unknown modded scene changes.

## Verification
dev\TestAuditCorrections.ps1 source + ValheimSOURCE call-path checks.
dev\VerifyEveryImplementation.ps1 runs the full existing tests plus
the new audit integration checks. dev\VerifyReleaseCandidate.ps1
checks 47 production source files and reject new per-frame hooks.
Runtime/subjective and real-world CPU gates remain open.

## Restore
backup\AUDIT_FIXES_20261122\Restore.ps1
Close Valheim before using it. Restores all 47 .cs source files, the
original project file, package DLL and Enngard installed DLL.
