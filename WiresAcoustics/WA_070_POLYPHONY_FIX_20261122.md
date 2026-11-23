# Wire's Acoustics 0.7.0: independent-loop polyphony investigation
Date: 2026-11-22
Status: installed development candidate; gameplay confirmation required; NOT released.

## Reported behavior
- With a brazier on the left and sconce on the right, only one flame is apparently audible; which one dominates changes as the player rotates.
- Two independent linen machines using the same recording similarly appear to collapse into one sound.
- This is reported as audible in-game, not merely a source listing or profiler presentation.

## Reproduced in source audit (confirmed code regressions)
1. 0.7.0 had silently DROPPED the existing 'Random loop start' behavior from 2026-11-21, while the Enngard config still contained 'Random loop start = true'. See backup/APERTURE_PROFILER_20261121/src/{Plugin.cs,Starts.cs}. That feature intentionally de-correlates nearby flames and machines that reuse the same loop recording.
2. The single-wall/source-shape code in Area.cs applied a second, arbitrary size gain to fireplace loops: FireLevel(size) was -14 to -18 dB for many small flames. This was on top of authored volume, inverse-distance gain, diffraction and HRTF. No such extra size multiplier is required for independently mixing the sources.
3. Neither current Prefabs/Filter/Budget nor Occlusion contains a one-voice-per-clip-name cap. Every live AudioSource has its own AcousticFilter and should be mixed independently.
4. Unity has a real-voice budget, raised 32 -> 128 by the mod in this session. Voice virtualization, output routing, and masking still require an in-game observation; cannot assume they are disproven by static source.

## Changes to dev build
- Plugin.cs: restored the configurable Random loop start flag (defaults true, existing Enngard config already true).
- Starts.cs: restored per-independent-world-loop random sample offsets on sound Play, and applied it BOTH to normal starts AND held/deferred Starts worker results. The November 21 implementation did not consistently cover deferred starts.
- Excluded unplaced 2D ambience, music, world random ambient, UI, voice, and outside loop types; skip clips under 0.75s.
- Area.cs: removed arbitrary fire-size source-level multiplier; retained 3D emission size, flame sound origin and source spatial width. No change to ordinary volume/clip data.
- AcousticTrace.cs: debug-only census of simultaneous looping AudioSource instances grouped by clip, including muted/nonplaying/virtual voices, output meter RMS, filter gain, position, priority, clip phase/sample and HRTF. Prioritizes fire and propeller names; not active when Debug log=false.
- dev/AcousticTraceTests/Program.cs: synthetic 3-instance trace exercise: 2 playing (one virtual), one stopped.
- dev/TestPolyphonyRegression.ps1: static regression check; included in normal VerifyEveryImplementation suite.

## Static verification
- dotnet Release build (net472): 0 warnings, 0 errors.
- AcousticTraceTests: PASS.
- All VerifyEveryImplementation tests: PASS.
- VerifyReleaseCandidate gate: PASS (including added polyphony source contract).
- No game runtime validation of 2-machine/2-flame positional mixing in this build yet. Does NOT prove audible issue resolved.
- No current profiler result for this exact build. Debug logging remains OFF for normal 0.26 ms timing comparisons.

## Narrow user replay test
- Return to same spot between 2 linen machines and between brazier and sconce. Slowly rotate with the camera while standing still.
- If merging is still audible: temporarily turn Debug / Debug log ON in Configuration Manager, turn several times and keep 10-15s of BepInEx/LogOutput.log, then turn Debug log OFF again.
- Compare [acoustics.polyphony] and [acoustics.polyphony.source] entries for sfx_fire_loop / SFX_propeller: instances, actual playback, virtual flags, volume, phaseSamples, targetDSP, outputRMS, HRTF.
- If 2 independently playing and independently metered instances are present yet only 1 is heard, next investigate HRTF/Unity mixer and correlated spatial signals. If 1 is virtual or muted, investigate Unity voice budget/priority. If sources share phase samples, investigate start paths bypassing ZSFX.Play.

## Backup and rollback
Backup directory: backup/LOOP_POLYPHONY_20261122
Restore before this pass: powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\kajet\WireMods\WiresAcoustics\backup\LOOP_POLYPHONY_20261122\Restore.ps1
No public publish occurred.
