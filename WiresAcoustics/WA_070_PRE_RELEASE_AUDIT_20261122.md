# Wire's Acoustics v0.7.0 — pre-release audit and candidate
Date: 2026-11-22
Status: RELEASE CANDIDATE, NOT PUBLISHED. Last required user acceptance: in-game active rainfall outdoors/under built roofs/near openings, plus optional CPU profiler.

## User direction
Owner explicitly rejected experimental loop fade (no audible improvement); remove entirely. Preserve all regular playback, independent equal-sound voices, randomized sample start, sound-range-derived fire levels. User reports general in-game sound quality good.

## Cleanup completed
- Removed the entire loop-start/stop fade feature by restoring pre-fade Filter.cs, Starts.cs, GeometryEvents.cs, Budget.cs: no detached tail AudioSource, OnDisable/Stop Harmony hooks, fade DSP callback, fade state, or tail-cleanup code.
- Kept AudioMan.UpdateLoopingConcurrency postfix protecting independent spatial sources from vanilla nearest-only selection, and individual timeSamples randomization.
- Removed unused Occlusion.MassLaw, MassCut, Mass, Thickness helpers. Live material transmission continues via Steam Audio energy fractions and SpectralPaths.
- Removed unused Budget.Silence legacy path, idle s_muted counter, AcousticFilter.Mute and OwnPriority; no actual active muting remained.
- Corrected AcousticFilter.OnAudioFilterRead to pass through unmodified when m_active == false; previously it could still apply stale DSP when marked inactive.
- Corrected obsolete Batch source comment; replaced stale package README claims about collider-size fire strength, hard obstruction thresholds and per-wall fixed penalties.
- Removed the no-longer-applicable TestLoopFades.ps1 and the temporary decompiled analysis from dev/ into backup/PRE_RELEASE_AUDIT_20261122/dev.
- Adjusted TestAuditCorrections regression and VerifyReleaseCandidate gate to reject dead mute and any reintroduction of rejected loop-fade code.
- Added 0.7.0 changelog summary and updated README to reflect independent loops and release testing status.

## Package issue found
Before audit the manifest had dependencies: [] despite requiring BepInEx. Added the currently listed Valheim BepInEx pack:
denikson-BepInExPack_Valheim-5.4.2360
Thunderstore source: https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim
The pack is a hard loader dependency; voice chat and Stackify remain optional.
Manifest v0.7.0, 256x256 icon and root ZIP contents were validated.

## Verification
- dotnet build Release net472: 0 errors / 0 warnings.
- dev/VerifyReleaseCandidate.ps1: RELEASE CANDIDATE STATIC GATE PASSED. All nested VerifyEveryImplementation tests PASS across 50 production C# files.
- Built and Enngard-installed and package DLL SHA256:
  13FAC68BDA1CCBCA1396998FAF35A7DCD287661437512273195410BE20257301
- No new Update/LateUpdate/FixedUpdate/coroutine/task job patterns introduced.
- Enngard: Debug log = false; Random loop start = true. Game closed for DLL deployment.
- Staged ZIP: staging/WiresAcoustics-0.7.0-RC-rain-pending.zip
  Zip SHA256: 2301BF36D9D9DFC72619917BBD2EA0407FC316B873558342157D0A3F8838F978
  Root entries: manifest.json, icon.png, README.md, CHANGELOG.md, WiresAcoustics.dll.

## Known open checks / scope
- Rain outdoors then walking under built roof, next to/opening-closing doors/windows, and reemerging. Do not publish until owner evaluates this.
- Runtime performance p50/p95/p99 and multi-mod environment were not reprofiled with this exact release-candidate DLL; automated tests cannot prove listening or CPU.
- Prior Enngard Unity log had 10 'Can not play a disabled audio source' warnings, without attribution to this mod or proof of audible impact.
- Numerical propagation is a bounded ray approximation, not full Steam Audio/Wwise; do not treat advanced engine parity as a release blocker.

## Recovery
backup/LOOP_FADES_20261122/ holds last pre-fade source and Enngard DLL.
backup/PRE_RELEASE_AUDIT_20261122/ holds the initial pre-cleanup and pre-package-edit materials.
backup/PRE_RELEASE_AUDIT_20261122/Restore_PreAudit_NoFade.ps1 resets to prior pre-audit nonfade code + DLL (Valheim must be closed).
Historic WA_070_LOOP_FADES_20261122.md is marked REVERTED.
No public upload, Git commit, unrelated mod modification or destructive repository cleanup was performed.
