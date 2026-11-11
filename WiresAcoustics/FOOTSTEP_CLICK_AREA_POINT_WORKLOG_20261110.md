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

## 40. Light HRTF (0.3.0)
- Before: light (Pan2) = occlusion + interaural level difference only. Selection by audibility and audio-thread load (Budget, 4..32 full).
- Bench (.NET 8 on the device, same loops, 1024 stereo frames): light 3.1 us, +ITD 6.3, +head shadow 6.9, +behind 8.3, +elevation notch (= full) 10.3. Relative only; Unity's Mono may differ. In-game log ratio full/light was ~4.6x (0.09 vs 0.02 ms/call), echo excluded.
- Change: light = Spatialize without the elevation notch (Pan2 removed). Echo stays full-only. Notch is ~7.5 kHz, mostly gone from far sounds via air low-pass already.
- Expected cost: light ~2.7x what it was by the bench. Budget's load control still applies.
- Backup backup/LIGHT_HRTF_20261111. Compiled, installed (sha256 74e33332...). Not tested.

## 41. Echo removed (0.3.0)
- Evidence: 2026-11-10 log: echo active in 2 room reports, "none" in 10; when on, 4-11% of DSP time. NeedsWork also forced processing of every sound within 50 m while an echo was around. Kajetan never heard it.
- Looked for a better unified approach online: Vercidium's ray traced audio and Sound Physics Perfected cast rays from the player (listener-centric), same as Room's 64-ray measurement driving the reverb. Steam Audio needs the spatializer chosen in Unity's project settings; no runtime route found.
- Removed: Bounces echo pick, Filter echo delay lines/pool/callback, Directions echo delay, Occlusion EnsureEcho, Room SetEcho and the sky-cover count, Budget echo drop/share. Bounces (first/second bounce paths) kept.
- Backup backup/NO_ECHO_20261111. Compiled.
- Installed (sha256 72e50fae...). Not tested.

## 42. Declick debug toggle
- Debug > "Fix sound clicks" (advanced, default on). Off: Prepare skips; every clip changed this session is UnloadAudioData + LoadAudioData (as CollectResources does), table clips checked against the original print, one log line with the counts. On again: re-repaired on next play (Fixed cleared, Searched cleared).
- Unproven: that UnloadAudioData/LoadAudioData restores the shipped samples. The log line checks it.
- Backup backup/DECLICK_TOGGLE_20261111. Compiled, installed. Not tested.

## 43. Realism pass: air absorption (ISO 9613-1)
- AirAt: was 22k*exp(-(d-5)/60), floor 2.5k. Now a table (every 2 m to 1 km) of the frequency ISO 9613-1 (20 C, 50% RH, 101.325 kPa) attenuates by 3 dB at that distance. Formula checked: 4.66 dB/km at 1 kHz, 159 at 10 kHz (reference ~5 and 160).
- Old vs new -3 dB point: 10 m 20.2k->14.3k, 30 m 14.5k->7.8k, 60 m 8.8k->5.3k, 100 m 4.5k->4.0k, 200 m 2.5k->2.7k, 400 m 2.5k->1.6k.
- C# table verified against the Python reference in a .NET harness.
- Backup backup/ISO_AIR_20261111. Compiled, installed. Not tested.
- Research findings not acted on yet (need Kajetan's call): walls far too transparent vs any measured wall (mod 1 wall -4.4 dB + 1.5 kHz low-pass; real walls STC 25+ (single plasterboard), timber stud 36, brick 43-55); corner/opening diffraction too weak (mod ~1 dB at 90 deg; Maekawa/ISO 9613-2 5-20 dB, capped 20); upwind wind loss too weak (mod -3.7 dB max; measured upwind/sidewind > 20 dB possible, downwind +few dB which matches). Reverb decay is Sabine already; ITD Woodworth, c 343 m/s, 1/r all correct.

## 44. Realism pass: walls, diffraction, outside indoors, wind
- Kajetan: do walls/diffraction/wind; calculate walls from thickness (stone 1 m, wood ~1/4) and material, measurements from the game files.
- Game files: woodwall BoxCollider 2x2x0.3, wood_wall_log 2x0.5x0.5, stone_wall_* 1 m thick. Thickness read per collider at runtime (min side of collider size x lossyScale).
- Walls: mass law TL = 20 lg(m f) - 47 (field incidence), m = density x thickness. Densities: softwood 500, hardwood/timberwood/ancient 700, stone/ashstone 2400 (masonry), marble 2700, iron 7870, ice 917. Grates excluded. Modeled as low-end gain 223.9/(50 m) + one-pole at 50 Hz (6 dB/oct = mass law slope). Wood 0.3 m: -30.5 dB <50 Hz, -50.5 at 500; stone 1 m: -54.6 / -74.6. Non-walls (terrain, rock, trees, furniture) keep the old soft table. Strength setting scales the wall dB.
- Diffraction (openings, corners): Kurze-Anderson form of Maekawa, D = 5 + 20 lg(x/tanh x), x = sqrt(2 pi N), N = 2 delta f / 343, cap 20 dB (ISO 9613-2 single edge). Gain = loss at 125 Hz; one-pole matched to the loss at 2 kHz. Table delta 0..50 m step 5 cm. Old Bend (~1 dB at 90 deg) removed. Path length gains: clamps (.35/.25) removed, plain direct/path.
- Outside indoors (Ambience): energy = (1 - enclosure) open + Room.WallEnergy (per level/up ray hit: Pass^2; wall mass law, furniture 0.9, terrain/rock 0, tree 1). Gain sqrt(total), cutoff energy-weighted log mix of 22k and 50 Hz. Openings' share = open/total drives the HRTF lean (cap 0.8 removed) and pan.
- Wind: dB = +3 x along downwind, 20 x along upwind, along = dot(wind x intensity, toward listener) x clamp01((d-15)/97). Was +/-35% linear clamp 0.65..1.3. Source: Waddington & Lam (IOA): downwind "a few dB", upwind/sidewind "in excess of 20 dB", shadow zone formed at the 112 m receiver.
- Unchanged and already physical: reverb (Sabine 0.161V/A via mean path), 1/r, ITD (Woodworth, 8.75 cm, 343 m/s), Doppler.
- Not done: random ambient calls still positionless indoors (the push-outside-and-route proposal); hills still the soft table (no top-edge geometry for a barrier formula).
- Backup backup/REALISM_20261111 (RestoreSource also deletes Diffraction.cs). Compiled, installed (2f1ab548...). Not tested.

## 45. Random calls from outside, hills as ridges
- Random ambient calls (3D) indoors: no longer made positionless. On start, Occlusion.Outside moves the spot along its own direction to 2 m past the first wall/floor/roof/ground within 40 m, unless it's already behind one or that way is open. Then the normal pipeline (mass law, Batch routes, HRTF from the opening). Ambience.Forget drops it from the ambience list and restores its pan. 2D ones still go to Ambience.Apply.
- Hills: ground hits no longer count soft 1; Occlusion.Ridge finds the highest ground point above the line (Heightmap.GetHeight, 15 samples, no rays) and applies Diffraction.Round over it. No ridge above the line -> stays soft. Works per leg in Batch (each ray's own start).
- Occlusion.Block struct (Soft, Through, Terrain, BendGain, BendCut) replaces the soft/through pair.
- Backup backup/AMBIENT_RIDGE_20261111. Compiled, installed (c983b998...). Not tested.

## 46. Documented outdoor/indoor standards (Kajetan: proper documents, no guesses)
- Ground (Outdoors.Ground): ISO 9613-2:1996 7.3. Porous = everything plants grow on; hard = water (below sea level) and paving (paint mask b > .5), per 7.3.1 wording. Mostly porous -> eq (10) A = 4.8 - (2hm/d)(17 + 300/d) >= 0 (hm mean path height over 17 samples). All hard -> eq (9) G=0: -1.5 -1.5 -3q, q = 0 if dp <= 30(hs+hr) else 1 - 30(hs+hr)/dp. Mixed mostly hard -> 0 dB (per-octave only). Over a ridge -> 1 (A_bar = D_z - A_gr). Indoors (enclosure >= .35) -> 1.
- Forest (Outdoors.Forest): Hoover A = 0.01 r f^(1/3) (Arup Strutt, ISO 9613-2 foliage page), r <= 200 m (ISO limit). r = path share inside the game's forest: forest factor within a tree vegetation's (TreeBase prefab, m_inForest) threshold range for the biome. Gain at 125 Hz, one-pole matched at 2 kHz, combined into AirCutoff by min. Trunk hits no longer count soft (forest covers them).
- Critical distance: D = 0.057 sqrt(QV/RT) (Arup Strutt), Q 1, V = 4pi/(3N) sum r^3 over room rays (MaxDistance on misses), RT = Sabine decay. reverbZoneMix curve dist/(dist+D) unchanged (Unity's send semantics unknown).
- Underwater: T = 4 Z1 Z2/(Z1+Z2)^2, Z air 415, water 1.48e6 (DOSITS; formula: normal-incidence notes) -> -29.5 dB. Cutoff 400 removed (undocumented).
- Sea: line source 3 dB/doubling (HK EPD). Unity custom rolloff sqrt(min/r), max = min x 200; FadeAt as other physical sounds.
- Skipped, no quantitative documented model found: night temperature inversion (only a single 5.5 dB @ 500 m data point, COMSOL; JCAA review qualitative), weather in air absorption (no documented mapping from Valheim environments to temperature/humidity).
- Numbers checked in Python: underwater -29.5 dB; ground porous 50 m hm 1 -> 3.9 dB; hard 100 m -> -4.2; forest 100 m -> 5 / 12.6 / 20 dB at 125/2k/8k; critical 75 m3 RT .5 -> 0.7 m; line 80 m -> -3 dB.
- Backup backup/OUTDOOR_STANDARDS_20261111. Compiled, installed. Not tested.

## 47. Game values -> real values (Kajetan: anchor game values to real ones and convert)
- Air: 3 ISO 9613-1 tables chosen by EnvMan.IsFreezing()/IsCold() (the game's status-effect flags). Warm = Bergen-Florida July mean daily max 19.6 C; cold (Meadows nights are "cold" in the game) = July mean daily min 12.4 C; freezing (Mountain) = Finsevatn 1210 m January mean -8 C. RH 75.6% (Bergen July). Sources: Wikipedia climate tables (1991-2020 normals). -3 dB point at 100 m: warm 4.8k, cold 4.2k, freezing 2.3k. Rain/fog humidity not mapped (no source for rain RH; fog threshold would be invented).
- Wind: game env data (main.unity): SnowStorm wind 1.3-2, ThunderStorm 0.8-1, Clear 0.1-0.6. NWS blizzard = 35 mph (15.6 m/s) and up -> 1.0 = 12 m/s (Clear 1.2-7.2 m/s, ThunderStorm 9.6-12). Waddington & Lam: upwind levels off beyond ~4 m/s, downwind grows to ~7 m/s -> factors clamp01(-v/4), clamp01(v/7).
- Night inversion: still no quantitative model; a game->real mapping doesn't supply the missing physics.
- Backup backup/GAME_TO_REAL_20261111. Compiled, installed (0ad6f232...). Not tested.

## 48. Humidity from the game's fog and rain
- Fog: Valheim RenderSettings fog mode 2 (Exponential, main.unity). Unity exp fog = exp2(-(density/ln2) z) = e^(-density z) (unity_FogParams docs + Shader Graph Fog node). MOR (5% left, IALA/WMO) = ln 20 / density. Fog < 1 km (RMetS, Wikipedia Visibility) -> RH 100 (fog forms near 100%, Wikipedia Fog); mist 1-2 km -> 95 (WMO > 95%, RMetS). Valheim's shader code isn't in the export (stubs), so that its fog uses Unity's standard formula is from the fog mode setting, not read from the shaders.
- Game data -> visibility: Clear day 0.003 -> 999 m (fog by a metre), Clear night 0.01 -> 300 m, Heath 0.006 -> 499 m, Misty night 0.15 -> 20 m, rain 0.03 -> 100 m. So by this mapping Valheim is foggy nearly always.
- Rain: LIAISE (MDPI Remote Sensing 18(3):509): moist pre-rain air (> 72%) -> 85-90% within ~30 min of onset; no clear difference by rain rate. 87.5% midpoint. Ramp 30 real min in game time = 30 x 60 x dayLength/86400 (dayLength 1800 s in main.unity -> 37.5 s). After rain: no measurement found -> same pace back (stated as such).
- AirAt interpolates log-cutoff between humidity tables (75.6/87.5/95/100) per weather; tables built lazily.
- Backup backup/HUMIDITY_20261111. Compiled, installed. Not tested.

## 49. "Bass boosted, loses detail" (Kajetan's question) - vanilla mixer evidence (MasterMixer.mixer, Default snapshot)
- SFX group: Compressor threshold -9.8 dB, attack 10 ms, release 154 ms, makeup +1.6 dB; ParamEQ 401 Hz x0.8 (-1.9 dB, 1.2 oct), 1584 Hz x0.809 (-1.8 dB, 0.65 oct), 12 kHz x1.15 (+1.2 dB, 3 oct); its own SFX Reverb (Room -711 mB, decay 0.5 s, Room LF -4651).
- SFX_LARGE: 420 Hz x0.8, 8.4 kHz x1.2, compressor -1.1 dB, own reverb 3.5 s.
- Ambient: 223 Hz x0.79, low-pass 11 kHz and 15 kHz, compressor -10.4 dB makeup +3.
- Master: high-pass 25 Hz, compressor -1 dB.
- Only volumes are exposed parameters (4), so a mod can't change these effects with SetFloat.

## 50. Clean mix: round the game's mixer colouring
- Mixer hierarchy (MasterMixer.mixer): Master -> Effects (only Attenuation; exposed = SfxVol, set by AudioMan.SetSFXVolume) -> {Ambient, SFX -> SFX_LARGE}. Effects default 0 dB; SFX 0; SFX_LARGE 0; Ambient -4 (Menu: SFX -15, Ambient -15, SFX_LARGE -14.8).
- Mixer.cs: sources whose group is SFX/SFX_LARGE/Ambient are sent to Effects (keeps the SFX volume slider and Master's 25 Hz high-pass and -1 dB compressor as the clip limit). Ambient keeps its -4 dB via AcousticFilter.RouteGain (filter forced active). Routed in AcousticFilter.Awake (prefab filters cover nearly every world sound) and for AudioMan's three loops each frame. In menus (AudioMan.InMenu via delegate) all go back to their groups so the Menu snapshot's -15 dB still works; mod off -> back too (before ReleaseAll; also Plugin.OnDestroy).
- Not replicated: the compressors' make-up gain (+1.6 dB SFX, +3 dB ambient): part of the compression being removed. Loud transients will be louder than vanilla (no -9.8 dB compressor); Master's -1 dB compressor still prevents clipping.
- Setting: Debug > "Skip the game's mixer colouring" (advanced, default on).
- Backup backup/CLEAN_MIX_20261111 (RestoreSource deletes Mixer.cs). Compiled, installed. Not tested.

## 51. Clean mix in menus too (Kajetan)
- No switching back in menus. RouteGain now carries the groups' snapshot levels: SFX 0 -> -15 dB, SFX_LARGE 0 -> -29.82 (-15 SFX parent + -14.82 own), Ambient -4 -> -15, glided over AudioMan.m_snapshotTransitionTime (2 s) on unscaled time. Effects' own Menu change (-0.026 dB) still comes from the snapshot since Effects is in the path.
- Unity's mixer UpdateMode is Normal (scaled time) in the asset; ours glides on real time, so in the paused menu ours completes the fade where the game's could stall. Noted, not matched.
- Built while Valheim was running: not installed. Built DLL at backup/CLEAN_MIX_MENU_20261111/next.dll.

## 52. Own reverb (bus + renderer)
- IMPORTANT finding: the 16:05 test session ran build 881a26a2, whose new setting key "Skip the game's mixer colouring" has an apostrophe -> BepInEx ArgumentException in Plugin.Awake -> the mod did not load at all (LogOutput line 102). Kajetan's "sounds great, nothing cut off" was the game without the mod. Key renamed "Skip the game mixer colouring".
- Unity docs (OnAudioFilterRead): filter data is the clip or the previous filter's output -> pre-volume, pre-spatialization; a filter with no clip on a playing source is the source (procedural).
- Bus: sources (filter, before Process) add mono x SendRef (volume x minDistance, Directions per frame) x m_gain x global x RouteGain into one of two slots; renderer reads slot[tick], clears it, tick++ -> 0 or 1 buffer latency whichever order Unity runs them; measured by time since last send, taken off reflection delays. Overlap counter detects a second audio thread.
- Renderer: AudioSource (no clip, 2D, bypass zones, to Effects) child of AudioMan + ReverbRender. Early: planes from room rays (cluster normal dot > .95, offset within .5 m, >= 2 rays), image source 2d, gain sqrt(1-a)/(2d), Woodworth ITD + ILD per tap from the listener's turn (main thread each frame). Ground excluded outdoors (ISO ground covers it). Late: 8-line FDN (1009..2459 samples @48k), Householder, per-line one-pole damping (Jot) for RT at 250 Hz and 4 kHz; input / critical distance.
- RT per band: Sabine 0.04 mfp / (a + m mfp), m = ISO air dB/m / 4.343 at 250/4k in current weather. Per-band a (Sengpiel table): brick .03/.07 (stone), marble/tile .01/.02, wood on joists .11/.07 (wood), hardwood .04/.07, water/ice .008/.025, concrete/tile .01/.02 (paving). No per-band data: iron, furniture, props, rock, trees, natural ground -> single measured value for both.
- Python checks of the FDN: RT measured 1.21 s (target 1.2) low, 0.54 (0.5) at 4 kHz; steady level 0.8-1.03 of input low/mid across RT 0.3-3 s.
- Unity reverb zone disabled while own reverb is on (Debug > "Physical reverb", default on).
- Backup backup/OWN_REVERB_20261111. Compiled, installed (cf7f2287...). Not tested.

## 53. Strip (Kajetan: drop the fluff)
- Kajetan's test: no cutoff this time; a similar issue came from Physical reverb.
- Removed (his picks): own reverb (Reverb.cs, bus, sends, Room.OwnRoom/BandsAt; Unity zone always on again), ISO ground effect + forest absorption (Outdoors.cs, filter fields, Occlusion.Outdoor, Room.Paved), humidity from fog/rain (air tables back to one humidity, 75.6%), the debug toggles (Fix sound clicks + Declick.Restore/Touched, Physical reverb, Skip the game mixer colouring; the click fix and the clean mix stay always on).
- Kept: weather temperature for the air, sea line source, underwater, wind in ears, everything else.
- Removed files kept in backup/STRIP_20261111 (*.removed). Compiled, installed (8aa0bc03...). Not tested.

## 54. HRTF LOD removed (Kajetan, from the older-features strip)
- Evidence (his last session, build cf7f2287): light 0.090 ms/call vs full 0.097; audio thread 19-24% (budget pinned at its minimum of 4 full). The LOD saved ~7%.
- Removed: Budget's full-count adaptation, audibility sort, priority cutoff; Filter.Full, Crossfade/PrepareCrossfade/DSP snapshots, light diagnostics, AlwaysOn, Audibility. Spatialize always with the notch. Budget still switches processing on/off per playing sound and measures audio-thread time.
- Asked about other cuts (paths, portal hum, mist fix, travel delay, Doppler, widths, wind ears, sea, underwater): no answer / no preference -> kept.
- Backup backup/NO_LOD_20261111.
- Compiled, installed. Not tested.

## 55. "Deep fried sometimes" + "muffled loses detail" (open)
- Kajetan: the same sound is sometimes deep fried (landing on stone), linked to some impact; muffled sounds lose detail you'd hear irl.
- Facts: vanilla FootStep.OnLand picks the land effect by ground material only (no impact scaling). Stone land clips are ~70-75% energy < 100 Hz, land 4 has 7 clipped samples (file analysis), so they're bass-heavy as shipped, but that doesn't explain "sometimes". Repairs are in memory: disabling the mod without a restart keeps them.
- Unknown: whether output clipping (overlapping loud sounds, e.g. landing + fall damage sounds; no SFX compressor in the clean mix, Master compressor has 50 ms attack) causes it. Added OutputPeak (Debug log only): output peak dBFS and full-scale sample count per room report.
- Muffling detail: mass-law wall model is airtight (wood 0.3 m: -50 dB at 500 Hz); measured light walls are STC 25-36 (leaks/flanking). Not changed; open.
- Backup backup/OUTPUT_PEAK_20261111. Compiled, installed (803dc9c9...). Not tested.

## 56. Deep-fried steps: the declick tail cut
- Log (his session): output peak at most -4.7 dBFS, no full-scale samples -> not clipping. The sound he heard: sfx_footstep_climb (Player_Footstep_Climb_M_01..08), all Kind C in the table.
- DeclickMathC cuts everything above ~5 kHz from the tail (from where the step is 12 dB below its peak). Climb clips: tail from 48-167 ms of ~0.4 s; share of each clip's >5 kHz energy removed: 0, 24, 18, 7, 29, 17, 40, 10% (clips 1-8). Random clip choice -> "sometimes". Repairs stay in memory -> "mod disabled, still there" without a restart.
- Change: C clips replay the steps only (tailFrom = frames). Lead-ins unchanged. May bring back some tail crackle on wood/stone steps: to listen for.
- Backup backup/NO_TAIL_CUT_20261111. Compiled, installed (f7e56e49...). Not tested.

## 57. Still bad after removing the tail cut -> measure the steps, try point-only repair
- Harness (DeclickMathC.Replay with the table's steps on the shipped clips): steps change 2-19% of samples; highs above 5 kHz -0.1 to -4.9 dB, 2-5 kHz down to -5.6 dB (climb 7). Varies by clip -> still "sometimes".
- Kajetan's idea: repair only the click points. Prototype (dev listen/, ~/an/arfix.py on the VM): autoregressive click detection (AR(20) per 1024-sample block, prediction error > 8 x robust sigma, impact +/-3 ms untouched) and least-squares AR interpolation of only the flagged samples (the standard method in audio restoration). Touches 0.3-1.3% of samples, highs -0.0 to -0.5 dB (land 2: -1.4).
- Listening files: dev/listen/<clip>_A_game / _B_mod_now / _C_point_fix .wav, each played 3 times. Waiting for Kajetan's ears before changing the mod.

## 58. Click fix toggle again (Kajetan: to test whether it's the cause)
- Debug > "Fix sound clicks" (advanced, default on, key without apostrophes). Startup log: "[declick] click fix is ON/OFF". Off: Prepare skips and logs the first 25 clips it left alone; turning it off in game reloads every repaired clip from the game's files and logs how many are verified back to the original. Restart with it off is the sure test.
- Backup backup/DECLICK_TOGGLE2_20261111. Compiled, installed. Not tested.

## 59. Click fix removed (Kajetan confirmed with the toggle: it was the cause)
- Removed Declick.cs, DeclickMath.cs, DeclickMathC.cs (in backup/NO_DECLICK_20261111), the Travel.cs call, the setting, and the embedded DeclickTable.tsv.gz from both csproj files (src/ and the VM build dir). data/ and dev/ files left on disk.
- Changelog 0.3.0: "Removed: the crackle fix from 0.2.7..." (the mod's own fault, stated plainly); dropped the earlier tail-cut line.
- Compiled, installed (20223516...). Not tested.

## 60. Walls by measured ratings, OutputPeak removed (Kajetan: do all 3, more logical)
- Problem: the airtight mass law blocked far more than any measured wall (wood 0.3 m: -50 dB at 500 Hz, -62 at 2k): leaks/flanking carry detail in real walls. Kajetan: muffled sounds lose detail you'd hear irl.
- Data: WoodWorks Mass Timber Acoustics: bare CLT wall 78 mm STC 33, 175 mm STC 38 (NLT 22-24). Commercial Acoustics: solid-core wood door STC 30-35 (field 2-5 lower). Engineering ToolBox: 230 mm brickwork plastered both sides STC 55; frequency shape -9/-5/-1/+3/+7/+11 dB at 125..4k. NAHB: no published STC for log walls. UKY VAC: composite TL (leaks cap TL: 1% open -> 20 dB).
- Now (Occlusion.LossOf): wood by thickness 33->38 (log-interp, clamped to the measured range), doors 30, stone/ashstone/marble 55, iron/ice mass law (no data). Gain = loss at 125 Hz; one-pole at 318 Hz takes the measured extra 16 dB to 2 kHz. Ambience uses the 500 Hz loss.
- Wood 0.3 m: -29 dB @125, -37 @500, -45 @2k (was -30.5 <50 Hz, -50, -62). Stone: -46/-54/-62 (was -55/-75/-87). Door: -21/-29/-37.
- OutputPeak (debug) removed.
- Backup backup/MEASURED_WALLS_20261111.
- Compiled, installed. Not tested.

## 61. Soft obstacles and reverb mapping from data (Kajetan: yes to #2)
- Soft obstacles (rocks/static_solid, non-structural pieces, trees, props): Diffraction.RoundObstacle from collider bounds: over the top + round both vertical edges, Kurze-Anderson each, energy sum (ISO 9613-2 limited-width barrier). Gain at 125 Hz, one-pole matched at 2 kHz. Falls back to the old weight only when you or the sound are inside its box. Muffle table now only for bounce-path penalties and ground without a ridge.
  - Numbers: 0.5 m trunk midway on 20 m: about -2 dB lows, -3 dB at 2k. 3 x 2 m boulder: -2 dB lows, -8 dB at 2k.
- Reverb zone: DecayHFRatio = Sabine RT at 4 kHz absorption + 5 kHz air / RT at 250 Hz absorption + air; RoomHF = 10 lg((1-a_hi)/(1-a_lo) x air over one mean free path) in mB. Absorption per band: Sengpiel table (brick .03/.07 stone, marble .01/.02, wood floor .11/.07, hardwood .04/.07, water/ice .008/.025, paving concrete .01/.02); no per-band data -> single value.
- Not changed, no data: Room/Reflections/Reverb levels. Unity docs: the reverb send comes after volume and distance attenuation; the I3DL2 reference (Microsoft) defines Reflections/Reverb relative to Room but not Room's absolute calibration -> physically correct levels would need an in-game calibration. Forest reverb block (unsourced "measured" claim) also left.
- Backup backup/OBSTACLES_REVERB_20261111. Compiled, installed. Not tested.

## 62. Measurement, forest reverb, bounce penalties, per-band data (Kajetan)
- Bounces: the made-up penalty (absorption x1.5 + 0.5 x mirror deviation, in wall units) replaced. Energy = (1 - a)[(1 - s) mirror + s scattered] (Vorlander & Embrechts, Acta Acustica 2004); scattered part Lambert cos/pi (ODEON, Zeng/Lynge/Rindel 2006); mirror part only within the bounce ray's own cone (4 pi / 64 sr, 14.4 deg). Scattered from a patch the size of that cone: E/E_direct = s(1-a)(Omega/pi) cos(theta_out) d^2/r2^2; mirror: (1-a)(1-s) d^2/path^2. Second bounce: first one mirror (1-a1)(1-s1). Ranking by this gain.
- Scattering coefficients: ODEON Table 5 mid-range values: stone 0.075 (brickwork filled joints unplastered 0.05-0.1), log walls 0.15 (open joints 0.1-0.2), planks/iron/ice 0.035 (smooth 0.02-0.05), marble/water 0.0125 (smooth painted concrete 0.005-0.02), furniture 0.3 (bookshelf), ground/rock/trees 0.45 (rough structures 0.4-0.5).
- Per-band absorption added: rock = limestone wall .02/.03/.05 (pyroomacoustics database; that page cites no source per row), furniture: beds = cloth upholstered chairs .60/.77/.70, rugs/hides/furs = hairy carpet on felt .14/.37/.25, banners/tapestries = velvet curtains .12/.35/.36, other = wooden chairs .08/.10/.12 (250/500/4k). Snow 0.7 single now sourced (Ishida: compact snow 0.6-0.8). Not found per band: iron/metal, grass, soil, sand, trees.
- Forest reverb block removed. Only source found (Chalmers thesis 'The sound amplifying forest', 2010: T60 0.66 s @250, 1.67 s @500 Hz, coniferous, 7-10.5 m) is flagged unreliable by its author.
- Calibrate.cs: Debug > 'Measure the reverb' (one-off): mutes everything, noise 1 m in front (filter-generated), dry then zone at Room 0 with late only (decay 0.5/1/2 s) and early only; GetOutputData energy; logs reverb energy / dry. Zone updates paused while running, reapplied after.
- Backup backup/BOUNCE_DATA_20261111. Compiled, installed. Not tested.

## 63. First reverb measurement: inconclusive
- Kajetan ran it in his decorated wooden room (enclosed 100%, RT60 1.51 s by Sabine). Log: late 0.5 s / 1 s / 2 s and early only all +3.1 dB (energy x2.05) over the dry step, dry -16.9 dBFS.
- Identical across decay times and early-vs-late = the zone settings made no measurable difference; the 3.1 dB is between bypassReverbZones on and off, cause unknown. Can't use it.
- v2 (built, game was running, waiting at backup/BOUNCE_DATA_20261111/next.dll): baseline is the zone on but silent (Room -10000) on the same path; preset forced to User; reads back the zone's values per step and logs each step's absolute level; adds a bypass step at the end for comparison.

## 64. Per-band absorption: ground, trees, iron (2026-11-11)
- Sources: Nord2000 ground classes A-H (flow resistivity 12.5 / 31.5 / 80 / 200 / 500 / 2000 / 20000 / 200000 kPa s/m2, labels from d&b NoizCalc white paper 1.6 p.9; same values in Bies/Hansen/Howard Engineering Noise Control table 5.2 per arXiv 2205.13695). Delany-Bazley impedance, locally reacting, random-incidence average (computed in python, Paris integral). Note: X = rho f / sigma is below DB's 0.01 fit range for the hard classes, as in Nord2000 practice.
- Values (250 / 500 / 4k): A .824/.891/.923, B .663/.791/.924, C .459/.611/.904, D .286/.413/.824, E .165/.251/.664, F .066/.105/.369, G .013/.022/.091, H .002/.004/.018.
- Mapping (my choice, an anchor mapping, not data): Meadows/Plains C, BlackForest B, Mountain/DeepNorth A, Mistlands D, Swamp D, Ashlands F, Ocean sand E, tilled C, dug E, paved = concrete row.
- Trees: bark, Li, Van Renterghem, Kang, Verheyen, Botteldooren, Applied Acoustics 165 (2020) 107328: <0.1, 0.04-0.05 below 1 kHz, rising above 1 kHz but <0.1 up to 1.6 kHz. Used .05/.05/.1 (4 kHz not measured; carries the top of the measured range). Was .8 (unsourced).
- Iron: "Steel decking" .13/.09/.08/.09/.11/.11 (heyizhou.net absorption table). Was .04 single.
- Snow: Ishida 0.6-0.8 (single) vs class A .82-.92. Went with class A for consistency.
- Searched, not found: sand-specific flow resistivity with clean source (Attenborough IOA paper sandy soil value is OCR-garbled), per-band props.
- Backup backup/BANDS_20261111 (before.dll, Room.cs, CHANGELOG.md, Restore.cmd, RestoreSource.cmd). Built, installed (sha1 fba80a65). Not tested.

## 65. Reverb measurement v2: valid (2026-11-11)
- Baseline (zone on, Room -10000) -16.86 dBFS; bypassed -16.79: the silent zone adds nothing, so the baseline is clean.
- Room 0, late 0 mB, mix 1, at 1 m: reverb energy = 1.045 / 1.031 / 1.039 x the dry at decay 0.5 / 1 / 2 s. Early only (refl 0): 1.069.
- So: at 0 mB the zone gives back as much energy as went in, whatever the decay time. The level sets total energy, not the starting level.
- Physical target (Barron & Lee revised theory, consistent with the critical distance already used): reverberant/direct = 312 T r^2 / V * e^(-0.04 r / T); early part (1 - e^(-1.11/T)), late e^(-1.11/T). Needs per-source mix to rise with r^2, so the mix law and the mB scale must be known first.
- v3 added: mix 0.5 / 0.25 / 1.1, late +1000 / -1000, Room -1000. Mix re-asserted every step. Backup backup/CALIBRATE3_20261111. Built, installed (sha1 2146b3e9). Not run yet.

## 66. Measurement v3 and Barron reverb levels (2026-11-11)
- Correction to 65: the logged figure is energy ADDED / dry, so at 0 mB the reverb gives 2.06x (late) and 2.08x (early) the dry energy, not 1x.
- v3 (relative to late 0 mB, mix 1 = 2.056): mix 0.5 -> 0.257 (= 0.5^1.96), mix 0.25 -> 0.069 (0.25^2 = 0.0625): send scales amplitude. mix 1.1 -> 9.9x (+10 dB). late +1000 -> 9.95x, late -1000 -> 0.111x, Room -1000 -> 0.106x: millibels as documented.
- Barron (akutek.info, Theory and measurement of early, late and total sound levels in concert spaces): d = 100/r^2, er = 31200T/V e^(-0.04r/T)(1 - e^(-1.11/T)), l = ... e^(-1.11/T). 312 T r^2/V = (r/rc)^2 with rc = 0.057 sqrt(V/T).
- Zone: Room 0, Reflections = 1000 lg(early/2.08) + 1300, Reverb = 1000 lg(late/2.06) + 1300. Reverb strength setting adds to Reflections and Reverb.
- Per sound: mix = min(1, sqrt((r/rc)^2 e^(-0.04r/T) / 10^1.3)), r at least minDistance for logarithmic rolloff. Caps at 20x direct (r about 4.5 rc).
- Removed the occluded-sound mix boost (.3 + .25 occ, unsourced): the muffling sits ahead of the send, so what reaches you excites your room.
- Old mapping (reverbLevel from (1-a)/a, the -2600/-2200 lerps, dist/(dist+rc) mix) removed.
- Expect much more reverb in reflective rooms: e.g. the decorated room log (RT 1.44 s, rc about 0.8 m) puts a sound at 3 m about +10 dB reverb over direct. Physically consistent with that RT; the RT itself comes from the ray Sabine estimate.
- Backup backup/BARRON_20261111. Built, installed (sha1 4b0c12ab). Not tested.

## 67. "Very wet" (Kajetan) -> Eyring + room constant (2026-11-11)
- Log after 66: outdoors (open 75%, hits on sea 26-50 m) got decay 1.84 s and refl +637 / late +725 mB. Sabine + Barron assume a closed, lightly absorbing room; the old enclosure blend that kept the open dry was gone. My regression.
- Indoors: rooms ~8 m across (mean ray 4.2 m), a 0.10-0.14, decay 1.2-1.6 s, rc about 0.9 m. Bigger rooms at the same absorption give LESS reverb at a given distance (rc grows with size) but a longer tail (late share e^(-1.11/T) = 46% at 1.44 s vs 11% at 0.5 s). So size alone doesn't explain loudness; the long, bare decay does.
- Fix: Eyring decay (metricgate: Sabine overestimates above a 0.2-0.3, Eyring -> 0 at a = 1), also for the HF ratio. Critical distance from Arup's room constant R = S a/(1-a), D = sqrt(R/16pi), S = 4V/l. Indoors about the same (rc x1.06 at a .11); open spaces dry out.
- Not modelled (no quantity found): hearing's own reverb suppression (precedence, binaural). Reverb strength setting remains the honest knob.
- Backup backup/EYRING_20261111. Built, installed. Not tested.

## 68. "Still a LOT, like a cave, louder" (Kajetan) (2026-11-11)
- Found: ReverbMix used r = max(r, minDistance) for logarithmic sounds. Your own sounds (steps, swings, voice) sit 1-2 m away but have minimum distances of 2-5 m, so they were sent reverb as if 2-5 m away: e.g. at rc 1 m, 5 m gives 25x (capped 20x, +13 dB) vs 1.5 m giving 2.25x (+5 dB total). Wrong reading on my part: the dry you hear is the direct sound where you stand. Now r is the real distance.
- Evidence on the decay: furnished living rooms measure about 0.5 s (Perkin 1960) and 0.29 s at 2 kHz (BRE 1983, 47 rooms), per Burgess (IOA). Diaz & Pedrero (Applied Acoustics, 11,457 rooms): RT grows with volume. Ours: 1.2-1.6 s in rooms of a few hundred m3 with a 0.10-0.14. Size alone (T ~ size at equal a) would give about 0.6-1.0 s. So either Valheim rooms really are that bare (all wood, few soft things) or the ray average under-counts absorption (it weights by solid angle from your head, Sabine needs area weighting). Not changed: no evidence yet which.
- Backup backup/MIXDIST_20261111. Built, installed (sha1 1cd9f6c1). Not tested.

## 69. "Fix the 2nd one, all sounds reverb way too much" (2026-11-11)
- Found in config: Reverb strength = 0.034 (-29 dB) set during the earlier test. Kajetan needed that much cut.
- Area weighting (Sabine's average is by area): each hit counts d^2/cos (cos floored 0.1); open rays at 50 m as openings (a = 1). S = 4pi/N sum, V as before, mean free path 4V/S (was the mean ray distance), Eyring on area-weighted a, R = S a/(1-a). Same for the 250 Hz / 4 kHz decays and the HF ratio. Debug log now prints surfaces by area per kind (wood, stone, furniture, soft, ground, trees, rock, other) with their a, plus volume, mean free path, critical distance.
- Honest expectation: in an all-wood room this barely changes a (everything about 0.1), so the decay may stay long. 
- New suspect with no evidence yet: Unity reverb zones are FMOD's global reverb; the return may bypass the AudioMixer, so the game's SfxVol (10 lg(master x sfx volume)) would cut the dry but not the reverb. All calibration so far ran with the test sound outside the mixer. Calibration v4: silent/late 0 mB, straight vs through Effects; logs SfxVol and the dry difference.
- Backup backup/AREA_CAL4_20261111. Built, installed (sha1 d8d943e4). Not tested.

## 70. Reverted the Barron reverb (2026-11-11)
- Kajetan, strength 1: "still way too goddamn much... this is wrong". His hearing is the ground truth here; I kept defending the model instead of taking that. Room.cs, Occlusion.cs, Batch.cs restored from backup/BARRON_20261111 (pre-Barron, with the per-band data). Changelog lines for Barron, Eyring, own-sound distance, area weighting removed. Calibrate v4 (mixer route test) and Mixer.Effects kept, harmless.
- Last log before revert: wood 80-94% of area at a 0.12, volume 786-1344 m3 (the volume from cubed ray lengths is dominated by a few long rays: suspect), critical distance 1.2-2.0 m, decay 1.3-2.8 s.
- The Barron work is kept in backup/REVERT_BARRON_20261111 (Room.cs, Occlusion.cs, Batch.cs). Not to come back without an explanation for the gap.
- Built. Not installed yet: game running.

## 71. Why the Barron reverb was too much: inspection (2026-11-11)
- Math checked: zone level 1000 lg(part/2.06) + 1300 and mix sqrt(ratio/20) give wet = ratio x part, as intended. Listener is at the player's eye (GameCamera.UpdateListner), so distances are right. Vanilla sets mix only in ZSFX.UpdateReverb (our postfix overrides); ambient/wind/ocean/music loops bypass zones.
- Weak spot 1 (untested, strongest): every calibration ran with the test sound at volume 1, outside the mixer. Unity's tracker has reproduced bugs titled "Audio Reverb Zone still produces sound when the Audio Source volume is 0" (issue 16406) and "Audio Reverb Zone is not affected by the global volume setting when the Spatial Blend of the Audio Source is lower than 1" (14385). If the zone's return doesn't follow source volume / mixer volume, quiet sounds (ZSFX volumes ~0.3) and a turned-down SFX slider get the reverb many dB too loud. The pre-Barron levels were low enough to hide it; the +13 dB headroom exposed it.
- Weak spot 2: room volume from cubed ray lengths: 786-1344 m3 for rooms with a 4 m mean ray. A few rays out through gaps to far terrain dominate.
- Weak spot 3: Barron assumes the dry level is the free-field level of a real source. Game sounds are mixed to sound right dry.
- Calibration v5: volume 1 straight, through Effects, volume 0.25 straight. Built, installed with the reverted reverb.

## 72. Listener on the camera; room size through gaps (2026-11-11)
- Kajetan: "where the camera is, that's where YOU are". Listener.cs: prefix on GameCamera.UpdateListner keeps the listener at the camera (the game's own free-fly branch), vanilla when the mod is off.
- Room size: rays now first all cast, then a ray that isn't on a built piece (WearNTear) and ends past every built neighbour (Portals' 6-neighbour graph), or escapes with a built neighbour, counts as an opening at the farthest built neighbour's distance: absorbing (open), and its volume cone ends there. Outdoors (no built neighbours) unchanged. Space log shows volume and the number of gap rays.
- Backup backup/LISTENER_ROOMSIZE_20261111. Built, installed. Not tested. Calibration v5 still to run.

## 73. Calibration v5 result; "focused towards higher pitches" (2026-11-11)
- v5: late 0 mB adds 2.066x straight, 1.987x through Effects, 2.009x at volume 0.25 (dry -12.0 dB as expected); Effects dry +0.1 dB; SfxVol 0 dB. So Unity's reverb DOES follow source volume and the mixer. My suspect 1 was wrong (Kajetan was sure of it; the measurement says no). The Barron excess is not Unity's fault; remaining suspects: the room volume (since fixed, gaps) and the model's premise vs the game's dry mix.
- Bright overall sound: candidates from the code, none measured: the clean mix bypasses Ambient's 11 kHz and 15 kHz low-passes (wind, rain, sea get their full top) and both compressors; the SFX bypass is -1.2 dB at 12 kHz and +1.9/+1.8 dB at 400 Hz/1.6 kHz, so not brighter there. Room reverb HF is near 0 mB (RoomHF -6..-23) from the per-band data, brighter than vanilla presets.
- Added Debug > "Compare with the mod off": 8 s output spectrum (GetSpectrumData, both channels, Blackman-Harris, per octave) mod on, off, on again; logs on-off and the take-to-take spread. Coroutine on the plugin, started by the setting, no per-frame hook otherwise.
- Backup backup/SPECTRUM_20261111. Built, installed. Not run.

## 74. Clean mix removed (Kajetan) (2026-11-11)
- "Get rid of that whole thing": the clean mix was added to fix the deep-fried sound, which turned out to be the mod's own click fix. Bypassing the game's EQ, low-passes and compressors sounded off (bright, weird).
- Mixer.cs moved to backup/NO_CLEAN_MIX_20261111/Mixer.cs.removed. Removed: Mixer.Route in AcousticFilter.Awake, OrigGroup/RouteGain and their use in the gain and activity checks (Filter, Ambience), Mixer.Step (Room), Mixer.UnrouteAll (Plugin). Calibrate's through-Effects steps dropped (already measured). Changelog line "Improved: sound quality..." removed. Sounds play through the game's own groups again, with its EQ, compressors, low-passes, group reverbs and menu snapshot.
- Kajetan: the reverb on the reverted build is not overpowering.
- Built, installed. Not tested.

## 75. Clean-up, README (2026-11-11)
- Removed the one-off debug tools (Calibrate.cs, Spectrum.cs; settings "Measure the reverb", "Compare with the mod off"; Room's calibration hook and ReapplyZone). Their results are in 63-73. Kept in backup/CLEANUP_20261111.
- Removed dead code: Ambience.PlaceAsAmbient (never called) and the ConvertedAmbient/OldAmbientBlend restore it implied.
- AcousticFilter.OnAudioFilterRead: no Stopwatch or Interlocked work unless the debug log (or its meter) is on; those only fed the log.
- Stale text: Filter header (HRTF LOD crossfade, nearest-only processing), Reverb strength "1 = as measured" -> "1 = as designed, 0 = none".
- README rewritten for the current build (camera listener, measured walls, per-band materials, bounces, wind, sea, under water, places; echo, forest tail and HRTF LOD gone) with a Sources section. Changelog Performance line updated.
- Built, installed (sha1 b2581632). Not tested.
- 0.3.0 was released 2026-11-10 23:20 (commit a311070) with only the places/moving/wake/wind-in-ears items. Everything after went into a new ## 0.4.0 section; 0.3.0 and older now match the released file exactly.
