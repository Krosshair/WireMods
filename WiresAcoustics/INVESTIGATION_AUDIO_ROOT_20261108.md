# Wire's Acoustics — audio root-cause investigation (2026-11-08)

## User observations
- Footstep tiny clicks atop normal SFX, especially wood/stone constructed flooring, not noticeably natural ground; a 3 ms attack-only fade did NOT fix.
- Crypt/cave room reverb transition is now good, but exterior ambient rain/wind/track enters too late on exit and interior track persists too long. Transition must coordinate without arbitrary fading that hides the root issue.
- Swamp locationless ambient leech movement and fake neck/bird-equivalent SFX regularly cut off mid-clip on both start/end cycles. Check other biome ambient one-shots.
- User explicitly rejects speculative fade patch and wants real investigation.

## Rules
- Preserve source and installed baseline, do not publish or bump version without instruction.
- Investigate actual AudioMan, ZSFX, ambience loop, game event hooks and mod's source lifecycle.
- Distinguish known, suspected and verified through static analysis, game recordings or runtime log.
- Avoid permanent speculative DSP fixes, no per-frame polling additions.

## Investigation queue
1. Inspect current modified source / git status and previous test delta.
2. Trace `Travel.Start`: ZSFX.Play postfix Stop + PlayDelayed, and replacement of initialized runtime state; prioritize footstep crackle.
3. Inspect Ambience and Spawned, Follow, Filter, Occlusion for unexpectedly clipped ambient one-shots.
4. Trace AudioMan game ambient transitions / cave entering/exiting; inspect original decompiled methods if available.
5. Devise source-specific logging / experiment toggles, compile and install diagnostic build if safe.
6. Log conclusions and uncertainty here; leave test instructions and rollback.

## Findings
- Pending.

## Static evidence from decompiled Valheim assemblies
- AudioMan.UpdateRandomAmbient: spawns ambient ZSFX and sets either m_fadeOutDuration=m_forceFadeout (defaults to 3s) with m_fadeOutDelay=0, TimedDestruction timeout=fadeoutDuration+2; or if no force fade, fadeOutDelay=clip.length-1, duration=1, timeout=clip.length*1.5. This proves an intentional finite life, NOT that a particular audible cut is a fade. Important user correction: observed sound ENDS ABRUPTLY mid-effect, not gradually. Do not conflate.
- ZSFX.CustomUpdate: if isPlaying false, returns early; fade-out timer causes Stop() at duration; Stop() calls AudioSource.Stop(); TimedDestruction.DestroyNow destroys GameObject unconditionally when no ZNetView. Abrupt end could be either Stop/destroy at wrong time, natural clip ending, or DSP interruption.
- AudioMan.UpdateAmbientLoop: when a new clip is queued it first fades old AudioSource to 0 at dt/m_ambientFadeTime (default 2 s), then Stop, swaps the clip, then fades new in separately. Therefore 2-stage sequence delays new exterior sound even though Room's reverb response is faster. Wind has an independent volume update in FixedUpdate and an Indoor mixer snapshot transition default 2s. Need later align those separate systems without blanket fades.
- FootStep.RPC_Step chooses prefab based on explicit material/motion. Prefab names or audio clip names need not contain 'footstep'. Previous 3ms declick patch's string classifier probably didn't match wood/stone; the patch is removed. This means prior negative test is not diagnostic of whether attack discontinuities are causative.
- Travel.Start's Harmony postfix intentionally calls src.Stop then src.PlayDelayed(d) on EVERY placed ZSFX.Play. This is a plausible source of peculiar scheduling but not proven source of cracks. Test with targeted logging / controlled toggles before changing.
- Instrumentation added: AudioInvestigation.cs under src, event-only Harmony on FootStep.RPC_Step, ZSFX.Play/FadeOut/Stop, TimedDestruction.DestroyNow, AudioMan.QueueAmbientLoop/StopAmbientLoop, plus ambient snapshot in existing 3s Debug log. Uses Debug log flag already enabled in Enngard. No PCM processing or extra per-frame polling.
- Previous experimental footstep DSP patch moved to _removed/FootstepDeclick_failed_20261108.cs, removed config from Plugin.cs, version remains 0.2.3. Crypt reverb response change RETAINED because user explicitly says it sounds good.
- Diagnostic build compiled with zero errors/warnings and installed into Enngard with Valheim closed. Installed DLL SHA256: 032464C23197DD3C2F8BB38E998A406EEEB13E44CDB67FAA3175C11DDEF15C2E. Release archive and package DLL unchanged. Baseline installed DLL previously backed up under _removed/declick_reverb_test_before_20261108/; RestoreLive.cmd included.
- Runtime observations STILL REQUIRED; static decompilation cannot establish actual sound clip timings nor identify sudden cut root cause. Do not claim fixed.
## Next capture
1. Start Enngard profile, traverse wood and stone flooring, then outdoors natural ground. Reproduce the tiny pop.
2. In swamp, let 3+ random leech/neck background SFX cut. Logs should show RANDOM AMBIENT START/FADE/HARD STOP/DESTROY and associated clip lengths/preset forceFade/time playhead.
3. Walk cave/crypt boundary both ways: check AMBIENT REQUEST/STOP REQUEST and 3-s Snapshot for delayed cue and old-loop volume, plus reverb response.
4. After game closes inspect BepInEx LogOutput.log (no need to copy logs manually if still reachable). If current trace has Harmony patch failures, correct diagnostics before drawing conclusions.
5. Compare source/audio data for exact wood/stone clips once their names are logged; consider original sample discontinuity, buffer drops, forced stop. Avoid generic fades.

## Runtime log captured (2026-11-08 12:47:39, Enngard)
- BepInEx LogOutput.log 203725 bytes, diagnostics triggered successfully.
- Actual AudioMan.m_ambientFadeTime=10.00 seconds (!); ambient loop swaps sequentially (fade old 10s then fade new 10s); logged Rain_Loop11 -> Amb_Eerie_CaveLoop3 at t=20.30 and reverse at t=29.48. Definitive reason room-reverb transition and track handoff are badly mismatched; likely stale queued swaps too.
- Swamp uses BiomeAmbients Swamp forcedFade=0.00, therefore vanilla path sets fadeOutDelay=clip.length-1 and 1 second fadeOut; long clip: Underwater_WavePassby1 duration 1.42s, pitch .97, FadeOut logged at playhead .34/.40, HARD STOP repeatedly at 1.33 to 1.41 while isPlaying true, ultimately 0.00 false; TimedDestruction timeout 2.12. GiantBeetle_Growl2 1.02s pitch .93, repeated HARD STOP .90-.95; Splash_Water_Small1 1.14s pitch 1.02, HARD STOP 1.13. These confirm stop requests during playback but not by themselves an earlier-than-designed cutoff. Analyze Travel.Hold delayed-stop duplication especially on each ZSFX.CustomUpdate fade completion; Stop called repeatedly while Travel deferred pending stop. Need measure fade and sound signal against audible cutoff.
- Wooden step actual prefab fx_footstep_wood_jog with clips Player_Footstep_Wood_Walk_M_01..14, 0.50-0.62s. Old declick patch matches clip substring 'footstep' (yes: these DO match, contrary to earlier fear), and should have been eligible provided Harmony hook executed. However prefab-level name fx_footstep_wood_jog also matches. Negative result is meaningful but not conclusive on timing/coverage. Other materials vary.
- Diagnostic log has multiple swamp STOP requests; do not interpret every as actual AudioSource.Stop (Harmony prefix logs even when Travel.Hold returns false). The caller stack only shows Harmony wrapper; must add explicit logs in Travel.Hold indicating queued vs executed and actual playhead / delayed stop count to prove mechanism. 
- User specifically says swamp ambient sounds are CUT, not gently fading. Respect distinction; there is likely a stop or lifecycle issue separate from fades.
- No release publication; investigation ongoing.

## Implemented targeted known fixes (test build, 2026-11-08)
- Existing Room.cs fast reverb response preserved as user confirmed good.
- Room.cs: when a measured enclosure change >.12 and previous/new enclosure >=.35, temporarily cap AudioMan.m_ambientFadeTime (actual logged value 10s) at .25s. The vanilla old-then-new sequential fade will take roughly .5s instead of ~20s for a full old/new swap; settings restored after a 3s transition window. Outdoors, normal weather/biome fades retain original setting. Uses existing Room 0.2s measurement/tick; no new per-frame hook.
- AmbientTiming.cs: Harmony Prefix TimedDestruction.Trigger() zero-argument overload, runs at spawn-time after AudioMan.UpdateRandomAmbient assigns clip and fade settings. For game's default last-second random-ambient fade only (not forced-fade entries), set fade delay / duration and object lifetime from actual clip.length / abs(AudioSource.pitch) instead of nominal clip.length; prevents shortened/lengthened playback from ending at incorrect wall-clock time. No DSP fades added; re-times Valheim's already-existing fade. Cannot guarantee every audible cut is explained; this specifically addresses a proven timing mismatch in source.
- Travel.cs: once AudioSource.volume <= 0.0001 on Stop request, let vanilla stop immediately and remove obsolete queued actions. For still-audible delayed Stop/FadeOut events, deduplicate pending requests for the same ZSFX and retain earliest scheduled time. This resolves log-confirmed repeated stop attempts caused by suppressing Stop in ZSFX.CustomUpdate while audio source keeps isPlaying=true.
- Footstep DSP debouncer was already removed because ineffective; footstep SFX click remains open. Do not conflate with confirmed fade timing.
- Source modification includes existing AudioInvestigation.cs diagnostics, active only while Debug log=true; no new polling components.
- Built with dotnet net472 Release: 0 errors, 0 warnings. Git diff --check passed (CRLF conversion informational warning). Installed test DLL only in Enngard with Valheim closed. SHA256 AB5A66CE31CC2FCE46F68FA50E3701AD2536161BA7AC415C90C03716A0103204 matched build and installed.
- Prior installed DLL/source backup: _removed/audio_known_fixes_before_20261108. Rollback executable: _removed/audio_known_fixes_before_20261108/RestoreTest.cmd. No release ZIP, package DLL, manifest, changelog or version modified.
- Published 0.2.3 release ZIP SHA256 verified unchanged: A3C271BEA70B99E80097ABB3EE4086E2F0F315E4B9027079AA500F1B14A87884.
- Validation pending in game: enter and exit cave/crypt, confirm environment track changes within about half a second after game queues new track; swamp random ambient end smooth and complete at varied pitches; no recurrent STOP spam; see whether wood/stone crackle persists. Consider changing behavior if hearing/measurement contradicts.

## 0.2.4 release preparation — user-confirmed swamp workaround ineffective
- User confirmed the swamp ambient abrupt cutoff is NOT fixed. Remove the speculative ambient-clip pitch compensation entirely. AmbientTiming.cs moved out of src into _removed/AmbientTiming_unfixed_20261108.cs.
- Temporary event instrumentation AudioInvestigation.cs moved out of active src into _removed/AudioInvestigation_20261108.cs; Room debug summary no longer depends on it.
- Retained: room reverb quick boundary response, temporary fast environment-loop transition with original fade restoration, and Travel.cs deduplication of deferred stops / immediate stop once silent (independent correctness issue in earlier logs). Swamp cut itself is still open; footstep clicks still open.
- Updated package/CHANGELOG.md with 0.2.4 improvements and explicit known issues; source Plugin.Version and manifest version_number 0.2.4. Versioned docs/description unchanged except changelog.
- dotnet Release compiled successfully with 0 errors and 0 warnings; diff --check clean except Git CRLF notifications.
- Verified actual publish.cmd / tools/Publish.ps1; dry run: C:\Users\kajet\WireMods\publish.cmd WiresAcoustics -Version 0.2.4 -DryRun. Packed _releases/WiresAcoustics-0.2.4.zip (152950 bytes) and verified dry-run support for GitHub, Thunderstore 0.2.3 -> 0.2.4, Hexium 0.2.3 -> 0.2.4, and Nexus file 8320389. Dry run did not upload.
- Updated installed Enngard DLL with the compiled 0.2.4 test build (Valheim was closed). Actual live game/audio behavior still not verified. No public publication done by assistant.
- Real Windows CMD release command: cd /d C:\Users\kajet\WireMods && publish.cmd WiresAcoustics -Version 0.2.4
