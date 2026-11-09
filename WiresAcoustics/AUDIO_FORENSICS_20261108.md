# Valheim audio forensics — 2026-11-08

## Scope and evidence protection
- Investigating intermittent crackle/pop on wood/stone footsteps and other in-game effects; abrupt swamp ambient one-shots; delayed ambient loop handoff in and out of caves.
- No modification or deployment authorized/attempted during this audit. Current Enngard is WiresAcoustics 0.2.4. Existing earlier report: INVESTIGATION_AUDIO_ROOT_20261108.md (from detailed Nov 8 investigation).
- Current LogOutput.log includes WiresAcoustics initialization, spatial/reverb measurements, ProximityVoiceChat, but does NOT log audio callback underrun or a timed PCM discontinuity. A lack of FMOD/Unity audio errors cannot exclude hardware/output glitches or source-sample transients.

## Verified source facts / defects
1. Travel.cs: every placed ZSFX.Play that is already isPlaying is immediately AudioSource.Stop()'d, then PlayDelayed(distance/343). That is a real hard restart of the audio source on the main thread; it is not a sample-continuous scheduling operation. Potential source of onset click, but user has not supplied a capture correlating click to this path. This applies even to native Valheim SFX while mod is enabled.
2. Filter.cs: OnAudioFilterRead -> Process -> Crossfade can execute `new float[data.Length]` on Unity's realtime audio thread at first full-HRTF/cheap-HRTF switch or when DSP buffer changes. This is a verified realtime allocation and latency risk. The source's declaration that the audio callback never allocates is factually false for this branch. Need measure actual frequency and callback duration before concluding underruns.
3. Filter.cs Crossfade executes the OLD and NEW DSP variants on copies of the SAME input. It resets only m_gain and m_cutoff between the two runs, not old m_ring/m_pos/m_state/m_delay/m_gainL/R and filter histories, so the NEW variant processes using state already advanced by the OLD DSP for that buffer. This is a verified algorithmic continuity defect, especially around switching Full budget every 250ms (Budget.cs). The one-buffer amplitude crossfade does not prevent stale/advanced delay-line state. Audible consequence not yet confirmed.
4. Budget.cs changes per-sound Full HRTF quality every 0.25 seconds and supplies the Crossfade path. Counts adapt to callback time load. A possible correlation with tiny clicks, but not a demonstrated measured case.
5. Previous investigation's actual runtime log had AudioMan.m_ambientFadeTime=10s, with sequential 10s fade-out THEN fade-in; room-transition code subsequently capped it temporarily when enclosure changes. This establishes a specific source of late ambient loop arrival, unlike crackling.
6. Previous runtime test logged short swamp ambient clips Underwater_WavePassby1 1.42s, GiantBeetle_Growl2 1.02s, Splash_Water_Small1 1.14s with Stop invoked near playback end. Vanilla AudioMan uses fadeOutDelay=max(0,clip.length-1) with a 1s tail for ambient one-shots, and a finite TimedDestruction lifetime. Exact audible abrupt cut unresolved: calling Stop isn't always actual execution because Travel.Hold can postpone it. Prior pitch compensation did NOT fix user's audible issue and was removed.
7. Enngard has ProximityVoiceChat 1.0.5 capturing 'Microphone (SteelSeries Arctis Pro Chat)' at 48 kHz, plus Wire's Health Awareness audio and WiresAcoustics. These are additional audio clients; no evidence yet one causes crackle. Windows enumerates NVIDIA, Realtek, generic USB Audio and Virtual Desktop devices. No audio error entries found in recent Windows System error/warning events queried.
8. Current config still contains `Experimental footstep declick = true` but the 0.2.4 source removed the experimental declick config entry; stale BepInEx config text should not be interpreted as active feature.

## Required discriminating tests (not performed)
- Reproduce click with WiresAcoustics off while keeping the *same* vanilla sound and rest of mods. Differentiate intrinsic clip discontinuity / Valheim audio output from WiresAcoustics.
- Capture WASAPI loopback PCM or an in-game audio file around click. Inspect sample-to-sample discontinuity, both channels, amplitude and temporal correlation to footstep starts/stops and filter Full transitions. No recording available to inspect currently.
- Instrument AudioSource Play->Travel restart, DSP callback peak duration and first Crossfade allocation as event counters; avoid logging or allocating on the audio thread. Use aggregated counters on existing diagnostics reporting, not any new audio-thread logging. If needed, compare build A/B using source-only toggles.
- Check whether the actual Valheim audio assets begin/end at nonzero values rather than applying speculative fades; asset data has not been extracted yet.

## Confidence
- Realtime allocation and DSP state sequencing: confirmed from source (high confidence) but their role in perceived click is unproven.
- The 10s ambient handoff issue: directly logged, high confidence historically; in 0.2.4 a partial fix is already installed.
- Swamp abrupt cut and footstep crackle exact acoustical cause: not established without A/B or PCM capture; do not claim fixed.

## Follow-up quantitative evidence from 19:33 gameplay log
- Unity audio callback measured stereo, 2048 interleaved floats = 1024 sample frames, 48,000 samples/s => 21.33 ms buffer. These figures come from WiresAcoustics debug summary.
- WiresAcoustics reported audio-thread use ~11.1–15.2% during sampled 3s windows, usually 38–47 microseconds per callback, ~710–984 callbacks per report. This **does not indicate sustained DSP overload** by itself, but average percentage cannot rule out occasional long callbacks, Unity audio asset loads, device driver buffer starvation or GC pauses.
- Travel delayed ~2–17 new placed sounds per debug report; stops waiting=0 in the samples. Therefore the restart code is exercised frequently even when the deferred stop queue is empty. Presence is proven; causal audio crackle is not.
- Debug logs report `filters switched 0 times` most windows, so routine enabling/disabling a MonoBehaviour filter is NOT supported as a recurring source of frequent clicks. Full/light HRTF quality changes use separate `Full` flag and are not included in this toggle count.
- WiresAcoustics Filter.Crossfade allocates a new float[] on the audio thread only when first needed or buffer size grows, not every call. Latency risk is real but should not be described as constant allocation. Prior report phrase "DSP state sequencing defect" is a static hypothesis; old/new processing is by different algorithmic paths and some shared histories may be intentionally overwritten when re-priming. No proof of an audible click from shared state. The findings are prioritized as ALLOCATION RISK and CROSSFADE STATE AUDIT, not an established crackle source.
- Current last log: audio format and budget evidence available, but no raw post-mix audio capture; therefore no exact diagnosis of audible crackle should be declared.

## Recommended causal isolation protocol
1. Capture a short deterministic wood/stone footstep sequence with a WASAPI loopback recorder; do the same with mod disabled while keeping Valheim sound effects and other mods unchanged. If both recordings have same spike at same point relative to the step, inspect game source clip and source scheduling. If only modded version does, instrument Travel + HRTF transitions and A/B toggle them separately.
2. Record whether the abrupt swamp SFX ends at a PCM discontinuity versus a faded envelope (both can be perceived as "cut off"). Then correlate precise source name, pitch, clip length and requested/actual ZSFX.Stop and destruction.
3. For audio buffer xruns capture audio-thread *peak* callback time, not only current average reported by Budget, with atomically aggregated counters and no logging on audio thread. Crossfade branch first-use allocation and Full toggles should be event-counted.
4. Extract raw Unity AudioClips or access clips at runtime once, measuring beginning and ending samples of wood/stone footsteps and swamp one-shots. The installed game has resources.assets and resources.assets.resS; no asset extraction tool found on user's device during this audit. Unable to verify samples.
5. Do not silently change the active audio plugin until one branch has discriminating evidence. Dolby Atmos/Windows Sonic switching remains manual by user's preference.

## Additional verified concurrency hazard: echo pool ownership
- Filter.cs Echo() takes a local `float[] e = m_echo` on Unity's audio thread and writes its delay-ring. The main-thread `ReleaseEcho()` sets `m_echo=null` and immediately returns that same array to the reusable EchoPool. `DropEchoes()` may call this while Unity's callback still holds the old `e` reference, because checking m_echoLevel and m_active does not establish a cross-thread handoff/acknowledgement. Another source can pop and Array.Clear/reuse that same buffer while old callback is using it. `volatile m_echo` does not solve lifetime of an already copied reference. This is a true shared-buffer ownership race in source; it could produce intermittent echo contamination or transient artifacts. It is not proven to be the particular footstep click without A/B or signal capture.
- A safe future change would defer returning an echo array to the pool until callback quiescence is established (or use a separate immutable handoff-generation barrier). Simply setting it null before Stack.Push is not sufficient. Never clear/reuse a buffer still referenced in an active audio callback.

## Plugin and error cross-check
- Recent BepInEx startup lists Wire's Acoustics 0.2.4 and ProximityVoiceChat 1.0.5 as the two loaded plugins explicitly named for acoustics/voice. An old config file for ninefinger ImmersiveWeatherAcoustics exists, but no corresponding plugin load appeared in this log. The orphan config cannot be blamed for processing audio.
- Game warning `Only custom filters can be played... (Amb_MainMenu)` and `Missing audio clip in music respawn` occurred on main-menu initialization. Their presence is confirmed, but neither is an established explanation for footstep clicks or swamp clip cutoff.
- Previous wood footstep runtime asset identification: prefabs `fx_footstep_wood_jog`; clips `Player_Footstep_Wood_Walk_M_01..14`, approx 0.50–0.62s. Audio source samples and their discontinuities have NOT been extracted. An old 3ms fade experiment did not eliminate clicks; no current declick patch is active.
- 113 audio-budget windows in current log: measured mean 12.39% DSP time, highest 20.8% over full window. Therefore recurring high average DSP load is not demonstrated. Peak single-callback xrun timing is absent and remains a measurable gap.

## Overall assessment
- CONFIRMED: 10s sequential vanilla ambient loop fade used to delay cave/outside switch in earlier log; 0.2.4 contains a targeted timing change for it.
- CONFIRMED SOURCE RISKS: audio-thread float[] allocation during first Crossfade; unsafe return-to-pool lifecycle for echo delay buffers; hard Stop+PlayDelayed source restarting for distance propagation.
- NOT YET CONFIRMED AS AUDIBLE ROOT: which of those causes the tiny footstep pop, or whether raw vanilla step asset starts/ends with a nonzero sample; also sudden swamp ambient cut after 0.2.4.
- Mod files were not changed during this forensic pass, and no untested audio fix was installed.
