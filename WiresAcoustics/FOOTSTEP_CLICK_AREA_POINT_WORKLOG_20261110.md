# Wire's Acoustics — footstep start clicks + where shaped sounds are heard from, 2026-11-10

## 1. Footstep clicks (vanilla)
### Evidence
- Decoded every player footstep clip in ValheimSOURCE/ValheimProject/ExportedProject/Assets/Characters/Player/audio/wav/footsteps (wood 25, stone 26, grass 39, default, snow) with ffmpeg; full table in backup/START_FADE_AREA_POINT_20261110/footstep_clip_analysis.txt.
- Wood and stone clips mostly START on a large non-zero sample: first sample 0.30-0.93 of the clip's peak in most of them (e.g. Wood_Walk_M_03 0.76, Wood_Run_M_02 0.60, Stone_Walk_M_09 0.93, Stone_Run_M_04 0.81). Playback jumps from silence straight to that value: a click at the start of every such step. Grass clips mostly start at 0.000-0.005 (no click), matching "only wood/stone".
- Ends are clean (last sample 0 in all). DC offset negligible (<0.3% of peak). All 48 kHz mono, loadType 0 (DecompressOnLoad), so runtime clip data is readable.
- Explains why the earlier 3 ms declick (Nov 8) proved nothing: its name classifier didn't match these clips (INVESTIGATION_AUDIO_ROOT_20261108.md).
### Fix
- Filter.cs: `StartsMidWave(clip)`: first frame of the clip's own data (AudioClip.GetData, array overload via reflection) above 0.02 of full scale; cached per clip; only for loaded DecompressOnLoad clips. No names.
- Travel.PlayWithTravel (the game's accepted ZSFX play): such a clip arms a fade-in on its filter. In OnAudioFilterRead (before anything else, also when the filter isn't processing) the fade waits for the first non-zero sample (so a distance-delayed start is caught) and applies a 2 ms raised-cosine fade-in. Nothing else touched; any game clip starting mid-waveform gets it, not just footsteps.
- Only active with the mod on (vanilla clicks remain in vanilla).

## 2. Shaped sounds heard from the side facing you (user request)
- Area.Heard(f, listener): closest point of the sound's shape box (ShapeCenter/ShapeHalf, from the owner's colliders) to you, moved a quarter of the way toward the shape's middle; inside the shape: its middle.
- Directions: HRTF direction for shaped, unrouted sounds now points at that point (was the sound's own spot, often the object's pivot).
- Area.Points: the first occlusion check point is that point instead of the middle (a chest set into a wall: the middle is inside the wall, the front isn't).

## Build / install
- 0 errors/0 warnings. Live before matched backup before.dll. Installed SHA256 FDE83BDC6C9912421D384F79388D6649BACC417D84C80085EC69298C25CBBBBA.
- Rollback: backup/START_FADE_AREA_POINT_20261110/Restore.cmd, RestoreSource.cmd.
## Needs in-game test
- Walk on wood and stone floors: start clicks gone. Chests/hearths: heard from the side facing you.

## 3. Revision (user): lead-in instead of fade; heard from the most open point, never inside
- Fade-in replaced by a lead-in: Filter.LeadIn(clip) builds once per affected clip a copy with 2 ms in front, rising (raised cosine) from silence to the clip's first sample; the clip's own samples are untouched and play at full level. Travel.PlayWithTravel swaps the AudioSource's clip to that copy right before the game's accepted Play (mod on only). The trimmed-off start isn't in the file, so it can't be restored; this is the closest to "not cut". Memory: one copy per affected clip (about 50 footstep clips, roughly 6 MB).
- VanillaShortAmbient length match tolerance 2 ms -> 5 ms (the lead-in adds 2 ms).
- Shaped sounds: Area.Heard is the nearest point of the shape's surface, 5 cm outside it (no pull inward). Area.Points: the 4 occlusion points are all on the side facing you (nearest surface point, either side of it, its top). Batch records which point is least hidden (HeardLocal in the owner's space); Directions plays the sound from there. Unknown or own spot clear: nearest surface point.
- Build 0/0. Live before matched backup/LEADIN_OPENSIDE_20261110/before.dll. Installed SHA256 089B6B15FB0B0F68F0810CDF9E076D88E109DF1FDFB766AA2CF162091050AD05.
- Rollback: backup/LEADIN_OPENSIDE_20261110/Restore.cmd, RestoreSource.cmd.

## 4. Lead-in removed; the click the user hears is inside the sound (2026-11-10)
- User A/B (Downloads/Footstep_clicks): the click is still there with the lead-in, in the middle of the sound. Lead-in code removed (Filter.LeadIn, Travel swap; VanillaShortAmbient tolerance back to 2 ms). Shaped-sound open-side change kept. Installed SHA256 9C4FBD71668DB9DCCA715F2DAA3D6DF861EE7B2B8EDCCA8896B5340A82938BD4E; rollback backup/REMOVE_LEADIN_20261110.
- Stone_Walk_M_03 around the user's mark (~0.17 s): smooth decay, -50 to -60 dBFS, no discontinuity found.
- Discarded analysis: an FFT high-pass burst search flagged strong "bursts" in the last millisecond of many clips; those were wrap-around artifacts of the circular FFT filter (the clip's sharp start leaking to its end). Not real.
- Time-domain detector (second difference, 1 ms windows, 20 dB above the median of +-20 ms, above -60 dB re peak): wood 16 isolated spikes in 13 clips, stone 1 (Walk_M_09 at 20 ms), grass 8. Declicked test copies (spike window replaced by a 17-sample smoothed version, crossfaded) in Downloads/Footstep_clicks/declicked; script backup/REMOVE_LEADIN_20261110/declick_prototype.py. Waiting for the user's A/B before any in-game change.

## 5. Click located (user screenshot, Wood_Walk_M_03 at ~0.1735 s)
- The click is a ~0.2 ms high-frequency burst (about 10 samples ringing near 10 kHz, ~-24 dB re peak) riding on the low tail. The 1 ms detector diluted it and missed it.
- New detector (burst2.py): second difference RMS over 12 samples, 18 dB above the median of +-10 ms, above peak*2e-3. Finds 0.1737 s (24 dB). 63 bursts over 40 clips, none in grass.
- Repair (fix2.py): only +-1.5 ms around each burst is blended (Hann edges) into a 9-sample double moving average; everything else is untouched. Wood_Walk_M_03 max step near the click 0.030 -> 0.001. Files in Downloads/Footstep_clicks/declicked2 for A/B. Not in the mod yet.

## 6. Full removal pass (fix5.py, Downloads/Footstep_clicks/declicked3)
- User: most fixed in declicked2, some still click. Asked for full removal.
- Tail: once the body is 12 dB below its loudest point, everything above ~5 kHz (x minus a 9-sample double moving average) fades out over 10 ms and stays out.
- Everywhere else: anything 10 dB over its surroundings is smoothed, then re-checked, up to 6 passes with progressively heavier smoothing (9 to 55 samples), until nothing sticks out.
- The loudest sample +-3 ms is never touched (an earlier version without this cut the impact by up to 3.8 dB). Peak levels now unchanged in all 40 clips.
- Remaining above 10 dB: 4 clips, all grain in the impact body (Stone_Land_M_01 0.056-0.086, Stone_Walk_M_04 0.040-0.046, Stone_Walk_M_08 0.047, Wood_Walk_M_06 0.043/0.053).

## 7. Runtime declick in the mod (2026-11-10)
- src/DeclickMath.cs: C# port of fix5.py (no Unity calls). Checked against the Python output on all 40 wood/stone test clips: max difference -137 dB re peak or lower (float rounding), 3 to 29 ms per clip.
- src/Declick.cs: on ZSFX.Play (Travel.PlayWithTravel) for clips named Footstep_Wood / Footstep_Stone, mono, DecompressOnLoad: samples copied once with GetData, cleaned on a ThreadPool worker, written back into the same clip with SetData on the next wood/stone footstep. No extra memory kept, no polling. The first play of each clip is still the original. Skipped when the mod is disabled. Debug log: "[declick] <clip>: N spots smoothed".
- Built 0 warnings, 0 errors. Installed SHA256 1113449164170527A24CB13D2081357C22A7840EB5CC4AB1406407EE3BA1123B. Rollback backup/DECLICK_RUNTIME_20261110. Not tested in game.
- Online: no reports found of this click. One Steam thread (Jun 2024, no dev reply) on a "drip" noise at the end of footsteps and jumps.

## 8. Same click on other surfaces; declick extended to all player footsteps
- Scan of 3,574 game clips with the detector (backup/DECLICK_RUNTIME_20261110/all_clips_burst_scan.tsv) flagged many. User listened to Downloads/Footstep_clicks/other_surfaces and confirmed the same click on grass/default ("most sound effects have it, most noticeable on deeper ones").
- Default_Land_M_02 at 0.196 s: second-difference spike -26.5 dB re peak in the original, -59.3 dB after cleaning.
- Cause not proven. Tried a clean synthetic thud through libvorbis q -1..5: no bursts, so plain Vorbis re-encoding does not reproduce it.
- Declick.cs now matches names starting Player_Footstep_ (all surfaces, climb) or Foot_Mud_. Built 0 warnings, installed SHA256 381983c3275c185f7a4ff4bcb6addde4f2f5f4209797155d5b62216314d0b04c. Not tested in game.
- Not done (needs a decision): other sound effects. The detector also fires on intentionally clicky sounds (Seekers, Ticks, Dvergr machinery), so a blanket pass would smooth those too.

## 9. Declick on every sound effect
- User: "everything", the click is one specific type shared by the affected effects; Seeker clicks are a different sound.
- Declick.cs: no name filter. Every clip started through ZSFX.Play (all effect prefabs) that is DecompressOnLoad, 1 to 8 channels, up to 10 s. Each channel cleaned separately. If the clip was started looping, the tail cut is skipped (it would return at every loop point); burst smoothing still runs.
- DeclickMath.Clean gained a tail flag; with it on, the output still matches fix5.py on the 40 test clips (worst -137.5 dB).
- Not covered: sounds that don't go through ZSFX (music, UI clicks, streamed ambience loops). Footstep-only versions kept as backup/DECLICK_RUNTIME_20261110/*.footsteps_only.cs.
- Built 0 warnings, installed SHA256 416d83c62e630bafe33a72acfffba39b12873f641c8053831268a77774ca6be3. Not tested in game.

## 10. Click matched by shape instead of a blanket filter
- Clear tail clicks share one shape: a ring at ~6 to 8.5 kHz decaying within ~0.3 ms, 55-92% alike; peak level varies ~8x (0.013 to 0.103). Not one fixed waveform, one fixed type.
- Subtracting a fitted ring model alone (ring.py/ring2.py) only removed 5 to 15 dB, so the fit is used to identify the click and the old local smoothing to remove it (ring3.py).
- DeclickMath now: rough spots (10 dB over the surrounding 20 ms) -> fitted against a ring (rise/decay envelope, 4.5-9.5 kHz in 250 Hz steps, decay 2-12 samples, start -16..+5 samples) -> only spots where the ring explains >= 60% of the high band are treated -> 4 ms blended smoothing, harder each round, until the spot is <= 3 dB over its surroundings. No tail cut, no loudest-sample guard, no loop special case. Up to 3 rounds.
- Known clicks: now -6 to +3.6 dB relative to their surroundings (were +19 to +34). Rest of the clip changed far less than with the old pass (e.g. Wood_Walk_M_03 -41 dB vs -23 dB). Default_Land_M_02 0.196 s: +15.4 -> -0.5 dB. Seeker clips: 0 to 5 spots matched out of 34 to 155 candidates.
- C# matches ring3.py on all 40 test clips (same counts, <= -140 dB difference), 3 to 37 ms per clip.
- Built 0 warnings, installed SHA256 4b98308181fd1ed1c140abe83dccb8a54cb6b23b408ba2155616b499618935cd. Not tested in game. Previous (smoothing-all) sources: backup/DECLICK_RUNTIME_20261110/*.smoothing_all.cs.

## 11. In-game: "clicks are still there" -> first plays were unprocessed
- Log (Debug on): declick ran, 50 clips processed during ~30 s of play, many with 0 matches. Each clip's first play was always the original by design, and in a short test most steps are first plays of their clip.
- Declick.cs: on ZNetScene.Awake every loaded DecompressOnLoad clip is queued (names with "Foot" first) and cleaned 3 at a time on worker threads; a played clip jumps the queue; clips with no matches are not written back. Pumped from sound starts (no polling). Debug: "[declick] N loaded clips queued", per-clip counts, "queue finished".
- Built 0 warnings; NOT installed (Valheim was running). Staged as backup/DECLICK_RUNTIME_20261110/prewarm.dll with InstallPrewarm.cmd.
- Open question: whether clicks remain after everything is processed (gate 0.6 may miss some; known clicks fit 0.58 to 0.87).

## 12. Hardcoded click table
- dev/DeclickTableBuilder.cs runs DeclickMath over every exported clip at its real rate and channel count and records each click (channel, sample, round, passes). 3,768 files: 1,533 clips with clicks (9,015 clicks), 2,082 clean, 153 outside 1024 samples..10 s. Replaying the records on segments matches the full search exactly (worst -146.7 dB). 38 duplicate names are byte-identical, no conflicts.
- data/DeclickTable.tsv.gz (60 KB; plain copy dev/DeclickTable.tsv, 236 KB) is embedded in the DLL (csproj EmbeddedResource). DLL 108 KB -> 174 KB.
- Declick.cs: on ZNetScene.Awake every loaded clip matched by name|samples|channels gets its clicks redone in place, reading/writing only Margin (136) samples around each group of clicks. Clips not in the table go to the old worker search (queued at world load and on first play) and are named in the debug log; listed clips with clicks not found in memory are named too.
- dev/DECLICK_CLIPS.tsv: every clip with click count, length, rate, channels, asset path, the effect prefabs that play it and the prefabs/scenes that use those (from GUID references in the exported project). 160 clips have no reference in any exported prefab or scene.
- Built 0 warnings, installed (game closed, log idle 30 min; also staged as backup/DECLICK_RUNTIME_20261110/table.dll, SHA256 f8ad3e2cc02261c6e008f92e15ec991b400460a5de330a8cee657ab98739260e, InstallTable.cmd). InstallPrewarm.cmd superseded.

## 13. Table matched nothing in game
- Log: "0 clicks removed from 0 clips", 1511 listed clips "not in memory", 560 loaded clips sent to the slow search (too slow under Mono to reach most of them in a short session). Cause: the key used the exported sample count, and the game's copies are shorter (Stone_Run_M_06 0.533 s in game vs 26304 samples = 0.548 s exported; Run_M_03 0.512 vs 0.519).
- Fix: key is now name|channels. Each entry with clicks stores a fingerprint (16 samples as int16 around its first click, dev/DeclickFingerprints.py). At world load the game's copy is searched for that fingerprint over the length difference +-64 samples; the best match within 0.004 sets the shift applied to every listed click. No match: clip goes to the slow search and is counted in the log.
- Debug log now also reports which shifts were found ("lined up with the table at: 0 samples xN, ...").
- Table 3,568 entries, gz 128 KB (fingerprints don't compress). DLL 243200 bytes. Installed SHA256 c950dc1ce2482a9a7516e7dfef4fcbcd01ab2a4f8c00370c77889d42d0ee87ef (game closed). Not tested in game.

## 14. Table applies; user still hears crackles (stone)
- Log: "1145 clicks removed from 273 clips in 59 ms", all lined up at shift 0 (the exported copies only have extra samples at the end); 2 didn't line up. Clips not in memory at world load are handled on first play.
- Session was mostly stone. Stone clips played: Run_M_11 x4, Run_M_06, Run_M_10, Walk_M_02/03/07/08/09 have no click-shaped spot in their data at all (Run_M_06 no candidate even at 10 dB). Same as the user's first stone mark (Walk_M_03 ~0.17 s): nothing in the data there.
- So the stone crackle is either in the data in a form this detector doesn't see, or happens during playback. Raw copies in Downloads/Footstep_clicks/stone_played for the user to check in the editor.

## 15. Stone clicks ring lower; detector and fit widened
- User: Stone_Run_M_11.wav in the editor has "the same click as wood"; indoor walking still clicks.
- Stone_Run_M_11: the 2nd-difference detector found no candidate at all. The click is at 0.2008 s (and 0.2257 s), ringing near 4 kHz; the 2nd difference weights that band down, and the fit bank started at 4.5 kHz. Earlier frequency survey already put stone clicks at ~3.2 to 4.8 kHz.
- DeclickMath: rough spots now measured on the high band (x minus the 9-sample double moving average, RMS over 12 samples) instead of the 2nd difference; ring bank 2.5 to 9.5 kHz. Python prototype ring4.py (rclean2); C# matches it (<= -136 dB).
- Stone_Run_M_11 0.2008/0.2257: +10.9/+14.5 dB -> -0.5/+1.6 dB over surroundings. Wood known clicks still -8.7..+3.9. Cleaned copy Downloads/Footstep_clicks/stone_played/Player_Footstep_Stone_Run_M_11_B_cleaned.wav.
- Table rebuilt: 2,720 clips with clicks, 40,529 clicks (was 1,533 / 9,015); replay exact (-146.7 dB). Fixed 3 lines corrupted at chunk boundaries of the builder run (re-ran those clips). gz 298 KB; DLL 413 KB.
- Built 0 warnings. Installed (user confirmed Valheim closed), SHA256 5D9E3EF11E85F2530C201F542976FC38D7F3599F9979B786B64544498A5F5E6A; previous copy backup/DECLICK_RUNTIME_20261110/table_v2.dll. Not tested in game.

## 16. Test build: play cleaned copies instead of editing in place
- User: B files ("as in game") are fine, the game still clicks ("works in theory"). Asked to try full replacement.
- Declick.cs ReplaceTest = true: the world-load in-place pass is skipped. On each ZSFX play of a listed clip, a cleaned copy (AudioClip.Create + SetData, table fixes replayed on the full clip after LineUp) is made once and src.clip is swapped to it before Play. Debug: "[declick-test] <clip>: cleaned copy made, N clicks, changed X dB re the clip, shift S", and a running count of swapped plays every 25.
- Built (warnings only for the test constant). Staged backup/DECLICK_RUNTIME_20261110/replace_test.dll + InstallReplaceTest.cmd; installed after the user confirmed Valheim was closed. In-place source kept as Declick.inplace_table.cs.

## 17. Why in place failed and the copies worked
- Replacement test: most clicks gone (one wood clip still clicked). In-place with the same table: no change. Same data, different delivery.
- Cause (Valheim source + Player-prev.log): Game.CollectResources -> Resources.UnloadUnusedAssets runs right after the world loads (called from ZNet and ZDOMan; log: "Unloading 101886 unused Assets" just after ZNetScene's prefabs were added), after sleeping (SleepText -> CollectResourcesCheck) and hourly. Unloaded clips come back from disk unedited when next needed, with the same instance id, so the per-instance "Seen" set skipped them. The test copies were the mod's own objects and never unloaded.
- Fix: no world-load pass, no per-instance memory for table clips. On every ZSFX play of a listed clip: read the 16 fingerprint samples (cached shift from a one-time LineUp per name); closer to the stored "after fixing" samples -> nothing to do; otherwise fix in place (segments only) and store the new "after" samples. Debug: "[declick] <clip>: N clicks fixed [again (the game reloaded it)]" with running counts.
- No copies, no audio added to the mod. Unknown clips still use the worker search (in place; a reload of those is not caught yet).
- Built 0 warnings, installed SHA256 D61956A52B716C94CC0ED390762D28663B2292540D54AE35AB186A11D9D38A3F (log idle ~4 min). Not tested in game. Replace-test source kept as Declick.replace_test.cs.

## 18. Live click filter on every sound effect
- In-place fix confirmed applied before play (35 clips, 0 needed re-fixing in that session), user still hears clicks: the table misses some. User asked for click filtering on every sound effect "for now".
- src/StreamDeclick.cs, called first in AcousticFilter.OnAudioFilterRead (every ZSFX sound has the filter via AudioProvenance). Per channel: low band = moving average of a moving average (13 samples each), high = rest. High-band RMS over 12 samples > 10 dB over its reference (rises with 30 ms, falls with 5 ms) and above 3e-4, while the low band stays under 2.5x its own reference -> high band dropped for 2 ms (attack ~4 samples, release ~2 ms). 24 samples look-ahead (0.5 ms latency). 5 ms settle after each start. State reset when the filter skips (inactive/silent). Allocation-free after the first buffer.
- Offline (dev harness backup/DECLICK_RUNTIME_20261110/StreamDeclick_test.cs, 1024-sample buffers, original clips): wood known clicks -20.6 to -31.9 dB, Stone_Run_M_11 -16.0/-12.6 dB; high band dropped 3.4 to 15.1% of the time; overall change -15.6 to -36.8 dB re the clip.
- Table fix (per-play check) stays as well.
- Built 0 warnings, installed SHA256 4c899d110e0d3866d4f451c4381895e3e49d4a1cb66fe7411080816afa2a39fb. Not tested in game. Filter.cs before: Filter.before_stream.cs.

## 19. Live filter removed; where the in-game crackle can come from
- User: live filter made menu clicks sound funny and didn't touch the crackle. Removed (StreamDeclick.cs moved to backup as StreamDeclick.removed.cs, Filter.cs hook gone). Rebuilt and installed (table per-play fix only).
- Every change to the clip data now verified applied in game (world-load pass, per-play in-place, replacement copies, live filter on each source's output), yet the in-game crackle stays. User's first stone mark (Stone_Walk_M_03 ~0.17 s): signal -60 to -70 dBFS there, nothing more than ~6 dB over its surroundings.
- Ruled out by code: footsteps cut off by TimedDestruction (fx_footstep_wood_jog: 1 s timeout vs 0.57 s clip at pitch 0.8-0.9 = 0.71 s).
- Not yet ruled out: crackle created after the clip (this mod's HRTF/echo/pre-echo taps, Unity mixer). Needs one vanilla comparison in game (mod disabled in Gale).

## 20. Vanilla confirmed; dumping the game's decoded clips
- User, mod disabled: crackle still there (vanilla). So the mod doesn't create it, and the cleaned data still doesn't remove it in game.
- Remaining gap: every click position came from the exported .ogg decode (ffmpeg); the game decodes its own FSB Vorbis data. Fingerprints matched within 0.004 only (loose). If the in-game PCM has its clicks elsewhere, the table fixes the wrong spots.
- src/DevDump.cs (TEMPORARY): on the first ZNetScene.Awake writes every loaded Player_Footstep_* clip, untouched, to BepInEx/WiresAcoustics_dump as float WAV. Installed SHA256 prefix 59a1f580 (also backup/.../devdump.dll). Remove after the comparison.
- Result (166 dumped footstep clips): game PCM equals the exported decode at lag 0, median -76 dB re peak difference (16-bit rounding; worst -34 dB in one clip). Game copies are shorter but the export's extra samples are all digital silence; every game clip ends at 0 (no truncation click). So the click positions in the table are where the game has them, and the per-play fix writes the right spots.
- DevDump.cs removed from src (backup/.../DevDump.removed.cs); installed DLL still has it until the next install (game running).

## 21. Copy test with the declicked3 processing
- User's theory: the engine keeps playing what it already has; editing the clip after it was played doesn't change what's heard. Plausible; also possible that the shape-gated detection misses the audible clicks (never ear-checked; declicked3 was).
- src/CopyTest.cs + src/DeclickMathC.cs (the smoothing_all/declicked3 math, matches fix5.py): at ZNetScene.Awake every loaded Player_Footstep_* / Foot_Mud_* clip is copied, cleaned on a worker, turned into a new AudioClip (AudioClip.Create + SetData) and swapped in at play. No edits inside the game's clips. Declick.Prepare goes only to CopyTest while CopyTest.On. Debug: "[copy-test] N footstep clips queued", "cleaned copy ready", count of swapped plays.
- Built, installed (log idle 3.5 min). Also staged backup/.../copytest.dll + InstallCopyTest.cmd. DevDump removed by this install.

## 22. Copy test result: clean copies still click in game
- Log: 190 footstep copies made (declicked3), 125+ footsteps played from them. User: C files are clean in the editor; in game the clicks are still there. So the clicks are added during playback, not by the data.
- Next: clean copies with the mod's processing off (Enabled=false switches off HRTF/occlusion/echo/travel/follow etc.), to split "this mod's processing" from "Unity's playback (pitch 0.8-0.9 resampling, mixer)". CopyTest now runs regardless of Enabled. Built: backup/.../copytest_vanilla.dll (not installed yet, game running).

## 23. Recording the game's output
- User: the in-game clicks sit at the same spots as in the vanilla recordings (so not voice stealing). fx_footstep_wood_jog has a single AudioSource (Unity playOnAwake 0, no clip): ZSFX.Play is the only path, and it's swapped to the clean copy. Contradiction with "still clicks" -> need what the game actually outputs.
- src/CaptureTest.cs (TEMPORARY): 15 s after world load, records 30 s from the AudioListener (final mix) to BepInEx/WiresAcoustics_capture.wav; CopyTest logs each footstep in that window (time from start, clip, copy/original, pitch, volume, distance) to WiresAcoustics_capture.txt. Copy test (declicked3 copies) still on, mod Enabled as usual.
- Installed (log idle ~4 min).

## 24. Two recordings of the game's output
- Capture 1 (39 steps, all clean copies): user listened in the editor, hears no clicks, but heard them in game during that session.
- Capture 2 (124 steps, mostly Stone_Run, all copies; user heard clicks "multiple times, not always, mainly on sound resets, more on wood"): no clipping (peak 0.44). 59 sample-to-sample jumps; all but 2 sit at a fixed position after a step start, the same on every play of the same clip (Stone_Run_M_08 11/11, M_12 11/11, ...). They are the clips' own impact edges (e.g. Stone_Run_M_08 at 22.7 ms: -0.84 -> -0.47 -> -0.04 of peak in two samples), present in the original and in the clean copy (the loudest sample +-3 ms is never touched). Nothing foreign found in the mix.
- Conclusion so far: data clean, final mix clean; what is heard live is added after the mix (late buffers at the device, or the OS/device chain).
- CaptureTest now records 60 s and the arrival time of every mix callback (WiresAcoustics_capture_timing.txt: position in s, ms since the previous callback). A callback much later than the 21.3 ms buffer period = the device ran dry = a gap that is audible live but not in the recording. Installed (log idle 3 min).

## 25. The remaining click: clips that start mid-waveform
- User marked ~8.0 s in capture 2. At 7.8342 s the mix jumps +0.014 -> +0.140 (L) in one sample: the start of Stone_Run_M_01 (logged 7.8293 s, +5 ms travel delay). That clip's first sample is +0.212 = 67% of its peak. Others: Stone_Run_M_09 89%, M_03 60%, M_02 36%, Wood_Run_M_03 61%, Wood_Run_M_02 60%, Wood_Run_M_05 40%; Stone_Run_M_07/M_10 start at 0.
- Matches the user's description (on sound starts, not every step, more on wood) and the first analysis of the day (wood/stone clips start at 0.3 to 0.9 of peak). The earlier 2 ms lead-in was judged while the in-clip bursts were still there.
- CopyTest: copies whose first sample exceeds 2% of the clip's peak get a 3 ms raised-cosine lead-in in front (rising to the first sample, nothing faded). Log line per copy says the start level and "lead-in added". Capture (60 s + callback timing) still on.
- Built, installed (log idle 5.5 min), backup/.../copy_leadin.dll.

## 26. Lead-in confirmed; proper in-place fix built
- User with lead-in copies: "can't really hear it anymore" (maybe one or two on wood). Capture 3 (60 s, 208 steps, all copies): the large start jumps at footstep starts are gone; remaining jumps at step times are the clips' own impact edges (same position every play). Big jumps left at 52.8 / 57.6 / 57.8 s are not footsteps (other effects starting mid-waveform). Mix callbacks: median 19.7 ms, max 40.4 ms (they arrive in pairs), none late -> no device underruns.
- Table v4 (dev/DeclickTableBuilder.cs -> data/DeclickTable.tsv.gz, 304 KB; plain dev/DeclickTable.tsv, build log dev/DeclickTable_build.tsv): 3,568 clips. Kind C (213 footstep clips: the ear-checked declicked3 cleaning recorded as tail start + 4,849 smoothing steps; replay exact), kind R (2,545 clips, ring-matched fixes from before), 802 with nothing to do. 366 clips start mid-waveform (first sample > 2% of peak; 50 above 30%), 98 of them footsteps.
- Declick.cs rewritten, all in place: per play, 16-sample print check (original vs repaired); repair = C replay or R replay, then for non-looping plays the clip is moved up to 3 ms later inside its own length (only over trailing digital silence; all 96 dumped footstep clips that need it have the room) with a raised-cosine rise to the first sample; one SetData. R clips without a start jump still only touch segments around the clicks. Unknown/changed clips: worker search + lead-in, written back on the next sound start.
- Removed: CopyTest.cs (backup), world-load passes. CaptureTest.cs still in (TEMPORARY) to verify this build; DeclickMathC.cs now only replays.
- Built (one obsolete-API warning in CaptureTest), installed SHA256 9CA6B5A4FC2CCD6B06F56B04A79B3B5A7A220ACD5387338C9ED866B0DB241A06, DLL 424 KB. Not tested in game.

## 27. Release prep 0.2.7
- User played the in-place build (log: 51 clips repaired in place, 33 lead-ins, none unknown) and asked to release.
- CaptureTest.cs removed from src (backup/DECLICK_RUNTIME_20261110/CaptureTest.removed.cs) and its hooks from Declick.cs. Plugin Version 0.2.7. package/CHANGELOG.md: 0.2.7 section (vanilla click fix, shaped-sound open side).
- Built 0 warnings, installed SHA256 05489e47cee9b3e5122592d2b1bcd59961944e8f9a080de8c03b5cde14794ed1 (421376 bytes). Rollback backup/RELEASE_0_2_7_20261110 (before.dll = the build with the recorder).
- Left on disk from testing (safe to delete): BepInEx/WiresAcoustics_dump (166 WAVs), BepInEx/WiresAcoustics_capture*.{wav,txt}, Downloads/Footstep_clicks.

## 28. Chest sounds still came from the chest's middle
- User: opening chests, the sound is still heard at the centre of the chest.
- Cause (game source): Container.SetInUse creates m_openEffects / m_closeEffects at transform.position with no parent, and nothing set Follow.Owner, so the sound never got a ShapeRoot: Area treated it as a point. The open-side logic (Area.Heard / Batch) only runs for sounds with a shape.
- Fix (src/Follow.cs): patch on Container.SetInUse sets Follow.Owner to the chest for the duration of the call (released in a Finalizer; an outer owner is kept). EffectList.Create's postfix then gives the sound the chest as its shape. A chest doesn't move, so nothing is followed (a cart's or ship's chest follows it).
- Same gap exists for other things the game plays with no parent (doors, crafting stations, fermenters, ...): not changed, to be proposed.
- Built 0 warnings, installed SHA256 1A5FF648A2D88BE1EA5090D872390F691FE5F447EE23B29D4F2D6A6C73D27B58. Still version 0.2.7 (not published yet). Rollback backup/CHEST_SHAPE_20261110.

## 29. Open side only as a backup
- User: the nearest/open point should only be used when the piece is otherwise muffled; an unobstructed sound keeps its normal source location.
- Batch already only tries the shape's points when the sound's own spot is blocked (occ > .05) and sets HeardKnown only when one of them is less hidden. The forced part was Directions: with HeardKnown false it used Area.Heard (nearest surface point) instead of the sound's own position.
- Fix (src/Directions.cs): a shaped sound is steered to HeardLocal only when HeardKnown; otherwise its own position, like any sound. Area.cs comment updated. CHANGELOG 0.2.7 line reworded to match.
- Built 0 warnings, installed SHA256 dfd62b9f1bf79a4d5ef91d64f135fc442884bc49740864e9c13c984cbc187aee (log ended with ZNet OnDestroy / Net scene destroyed). Rollback backup/OPEN_SIDE_ONLY_IF_MUFFLED_20261110. Not tested in game.

## 30. Source points: right-hand bias, chest lids, fire sounds
- User: muffled chests sound further to the right nearly every time; chests should sound from their top, sconces from their fire.
- Right bias (src/Batch.cs): the shape points were compared with a strict "<" in order near, right (Cross(up, toSound) = right), left, top, so equal occlusion always went to the right side. Now all four are measured, then ties (within 0.02) go to the near point, then the top, then the middle between the two sides if they tie, else the better side.
- Chest lid (src/Follow.cs, src/Area.cs TopOf): a chest's open/close sounds (owner set by the SetInUse patch) are moved to the top centre of the chest's collider box before anything else uses their position.
- Fire (src/Area.cs Voice): a sound that is a child of a piece with a Fireplace is moved, once, to the nearest enabled Light in that piece (the flame) when it is first measured.
- Built 0 warnings, installed SHA256 7E6265E95614E998C205E12B596B3034D97F664C27EDFDA3698733D91F1DD346. Not tested. Rollback backup/SOURCE_POINTS_20261110. CHANGELOG not updated yet.

## 31. Sound source overhaul (Spots)
- User: apply the source-point idea to all sources: footsteps where you step, weapon sounds at the weapon, holstering where the item goes, etc.
- Survey of the game's EffectList.Create call sites (271): footsteps are already made at the stepping foot (FootStep.OnFoot -> DoEffect at foot.position); hits at hit points; equipping at the hand; projectiles at their spawn point. Wrong ones: attack start/trigger/trail effects at the attack origin, which is the character's root unless m_attackOriginJoint is set (3,387 of ~3,400 attack entries in the exported prefabs are empty); unequip, pickup, drop (Humanoid, at transform.position) and consume (at the local player's position); piece sounds at the piece pivot (Door open/close/locked, Smelter ore/fuel/produce, Fermenter add/tap, Fireplace fuel/toggle, Ship sail change, Incinerator lever, ItemStand, Container move-item).
- New src/Spots.cs, called first in Follow's EffectList.Create postfix (now also takes basePos):
  - made at a Humanoid's root and the list is its current attack's start/trigger/trail effect (attack's or weapon's): moved to and attached to the weapon hand (left if the weapon is the left item);
  - made at a Humanoid's root inside an item call: the call's spot from Equip (unequip: the back slot the item goes to per VisEquipment.AttachBackItem's switch, armour 1 m up; drop: right hand; use/eat: helmet bone; pickup: the picked object's position);
  - unparented, no owner, made exactly at a networked object's pivot (OverlapSphereNonAlloc 0.5 m, ZNetView root within 1 cm, not a Character): that object becomes the owner and the sound moves to its lid (Container), flame (Fireplace: nearest lit Light) or box middle.
  - Sounds attached to a bone are placed via PlayerSounds.Place (Follow skips attached sounds).
- Equip.cs: Take(h, bone, point) also sets Spots; Release clears it. Area.cs: CenterOf, FlameOf, shared shape cache.
- Debug: first 40 moves logged as "[spots] <sound> -> <where>".
- Built 0 warnings, installed SHA256 B8E661DCDEC5581B8843C69CC905ADD7CCBC3C83EFAF00ADA817BB02F65182AE. Not tested. Rollback backup/SOURCE_OVERHAUL_20261110.
- Creature heads: Character.m_eye is set on all 161 creature prefabs in the export (none empty). BaseAI m_idleSound / m_alertedEffects (made at transform.position) and attack sounds of a creature with no weapon shown in the hand (VisEquipment m_rightItemInstance / m_leftItemInstance null: bites, claws) now attach to m_eye; calls keep Facing. A player attacking with nothing shown: right hand. Installed SHA256 f011c343430dda20075a4c5a9d41af6594991eaf039ff7f8b40edbdcc05649d4. Not tested.
- Split per sound (user: draugr and trolls groan and swing at once). For a creature's call or an attack made at the root, each sound object is classified from its ZSFX name plus its first clip name: Voice (vocal, voice, _vo_, growl, roar, snarl, scream, shout, grunt, bite, spit, attacking, taunt, breath) -> m_eye with Facing; Limb (swing, whoosh, whsh, swipe, swish, slash, claw, trail, stab, chain, lift, flurry, whip) -> the weapon hand, else the right hand, else the head; Ground (stomp, slam, impact, land, jump) -> left at the feet; other -> the weapon hand, a player's right hand, else the head. Word lists from the game's attack sound names (dev/DECLICK_CLIPS.tsv). Installed SHA256 d21e98594fbb57a0c204128630711d04f96a2f29ec76afe1f05377dcb2e02c3c. Not tested.

## 32. Everything that can move takes its sounds with it
- User: the grappling hook reload sound stays where it started; make all moving sources' sounds follow.
- Cause: AnimationEffect.Effect (animation events: crossbow and grappling hook reloads, many creature attack and movement sounds) Instantiates the effect at the event's joint and never parents it; none of it goes through EffectList, so neither Follow nor Spots saw it.
- src/Animated.cs: prefix that does the same as the game's method, then, if the animated object can move (Follow.Moves: a Character or non-kinematic Rigidbody in its parents), attaches each ZSFX in the spawned effect to the joint (skips a ZSFX object that also carries a ParticleSystem).
- src/Spots.cs: an EffectList sound with no parent and no owner made exactly at a character's pivot (Character.GetAllCharacters, 1 cm) gets that character as owner, so Follow keeps it with it (pets, animals' jumps, Player minor-action start effects, the hammer's build effect). Bow draw (m_holdStartEffect) and the tool's build effect (m_buildEffect) of the held item go to that item's hand.
- Ships and carts: their own pivot sounds were already made owners by PieceAt (moving rigidbody -> Follow).
- Footsteps stay where the foot landed (correct). Static pieces unchanged.
- Performance: no per-frame work added; sounds attached to bones/joints move with the hierarchy instead of Follow's per-frame list.
- Built 0 warnings, installed SHA256 cee92259e58f1dbbfdb788588676fb193fc28a6aacd84870caa185dc838ac632. Not tested. Rollback backup/SOURCE_FOLLOW_PASS_20261110.

## 33. Deer, wake/sleep, quick performance read
- Deer (AnimalAI): idle and alert calls already go to m_eye with Facing. Added MonsterAI m_wakeupEffects / m_sleepEffects (made at transform.position) as calls -> head. Built; NOT installed (log active 90 s ago): backup/SOURCE_FOLLOW_PASS_20261110/wake_sleep.dll.
- Performance, from the mod's own counters in the last session's log: main thread 25-30 ms per ~240 room ticks (~0.12 ms per frame on average), worst tick ~1.1 ms. The "rays (waiting for their jobs)" figure (~1.0 ms worst) is mislabelled: it times all of Room.Run, which includes Measure() (~0.95-0.99 ms worst) and, with Debug log on, the 3-second summary line built from ~15 Summary() calls. Jobs are completed a frame or 0.2 s after scheduling, so there is no real waiting. Audio thread 15-17% busy (45-48 us per callback, echo 8-11% of it).
- Unmeasured candidates: Doppler's postfix on ZSFX.CustomUpdate calls AudioSource.isPlaying and GetComponent<AcousticFilter>() for every ZSFX every frame (could cache the filter per ZSFX); the debug summary string is built even when only some parts changed.

## 34. Proven issues fixed
- "rays (waiting for their jobs)" now times only Bounces.Complete + the room ray job Complete (label "waiting for ray jobs"); Measure keeps its own counter.
- The debug summary (Debug log only) is no longer built inside Measure: Measure flags it and stores the few local values; Room.Run writes it at the start of the next tick, so the two costs don't land in the same frame.
- Includes the MonsterAI wake/sleep -> head change. Built 0 warnings; installed, SHA256 60a87fc089113a5d7a6a6c6239f54a102983725e43904bc2c559bebc0bbd7769. Rollback backup/PERF_FIXES_20261110.

## 35. Doppler lookup
- Doppler.Apply (postfix on ZSFX.CustomUpdate, every ZSFX every frame) called AudioSource.isPlaying and GetComponent<AcousticFilter>() first for every sound. Now: filter cached per ZSFX instance id (only non-null results cached, so a filter added later is still found; cache cleared past 2048 entries), and the managed Doppler == 1 check runs before isPlaying, so sounds with no shift leave without engine calls. Same result as before.
- Built 0 warnings; installed, SHA256 590f1f08cb05e18f13cbd5a077a4d6fccdd9fbd5c3d7e2ec194140b23e7571a7. Rollback backup/DOPPLER_CACHE_20261110.

## 36. Open-side backup removed; crafting at the station
- User: with sources placed properly, the "heard from the least hidden point when muffled" backup isn't needed. Removed: Area.Points/Heard/Surface, Batch's four extra rays per shaped sound and the tie logic, Filter.HeardLocal/HeardKnown, Directions' use of it. Muffling is the sound's own spot again (own colliders still skipped). Saves up to 4 rays per shaped sound per check. CHANGELOG 0.2.7 line about it removed.
- Crafting: InventoryGui plays the current station's m_craftItemEffects / m_craftItemDoneEffects / m_craftItemDoneFailEffects at the player's position; Spots now moves them to the station's box middle and makes the station the owner (no follow).
- Built 0 warnings; installed SHA256 f087e70d4f861f7fd8d8791a5c114e7da19b6b15599582430e733b33a4529fcc. Rollback backup/NO_OPEN_SIDE_20261110.

## 37. 0.3.0 gaps
- 0.2.7 was published at 22:01 (_releases/WiresAcoustics-0.2.7.zip). package/CHANGELOG.md: 0.2.7 and older restored byte-identical to the published zip; new 0.3.0 section for everything since. Plugin Version 0.3.0 (manifest is set by publish).
- Doppler for sounds outside ZSFX (Kinds.Other: ship wake/water, shield hum): their Unity Doppler is off while placed and nothing applied ours. Doppler.ApplyOther from Directions (main thread, per frame, only for Other): pitch = base * Doppler, base re-read if the game changed the pitch in between; restored on release. Travel delay not added: these are loops started once.
- Piece child sounds left at the piece's pivot (prefab didn't place them) move to the box middle in Area.Voice; fires still go to the flame; characters excluded.
- Built 0 warnings; installed SHA256 853768fba857b7abcbe56b6c19d5e715aaf18f4572c34fb8c1ab179fad3d19a6. Rollback backup/GAPS_030_20261110.

## 38. Wind in the ears (0.3.0)
- Request: sound changes by which way you face the wind.
- Game: AudioMan.UpdateWindAmbience sets m_windLoopSource volume/pitch every frame; 2D loop, same in both ears.
- Ambience.Pan (already per frame from the AudioMan.Update postfix, no new per-frame hook): wind comes from -EnvMan.GetWindDir() (flat). Amount = wind intensity * (1 - Enclosure*1.5), eased, max .6.
  - HRTF on: wind loop gets Lean toward the wind (Lean now takes an amount). Openings lean wins indoors when stronger.
  - HRTF off: wind loop panStereo = dot(right, windFrom) * amount.
  - Volume: +2 dB with an ear to the wind, -3 dB with your back to it, scaled by amount. Applied after the game's set, so no compounding.
- Gated by Occlusion setting.
- Backup backup/WIND_EARS_20261110 (before.dll, Restore.cmd, RestoreSource.cmd). Compiled, installed (sha256 b3103462...). Not tested.

## 39. Ocean while paused (0.3.0)
- Cause: vanilla UpdateOceanAmbiance runs from AudioMan.FixedUpdate; pause sets Time.timeScale 0 (Game.cs 1187), FixedUpdate stops, so Ocean's postfix (shore placement, wall checks, HRTF setup) froze.
- Fix: Ocean.Step shared by the FixedUpdate postfix and a new AudioMan.Update postfix that runs only when Time.timeScale == 0. Timers now on Time.unscaledTime. Follow lerp frame-rate corrected in the paused path. No extra work in normal play.
- Backup backup/OCEAN_PAUSED_20261110. Compiled, installed (sha256 f86f6ade...). Not tested.
- REVERTED: user tested, didn't fix it. Source and changelog restored from the backup, before.dll reinstalled.
