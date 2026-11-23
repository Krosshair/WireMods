# Wire's Acoustics 0.7.0 - Independent gameplay log audit
Date: 2026-11-22
Method: Read-only audit of latest Enngard BepInEx/LogOutput.log and local source; no acoustic listening impressions used. Existing dev/AnalyzeAcousticTrace.ps1 also executed, writing ACOUSTIC_TRACE_REPORT.txt. No DLL/source/config edits, no release.

## Session / observed counts
- About 203.08 seconds game time; 49 diagnostic snapshots; 1,114 top-source observations (not unique sounds).
- 5,760 retained gain/filter transitions; 362 retained geometry events.
- Source-verified log history loss: 13,052 transitions and 218 geometry events overwritten (13,270 combined); approx. 68.4% of total attempted events (13,270 / 19,392). Per-window counters reset in AcousticTrace.EmitTransitions/EmitGeometryChanges.
- 1,046 spatial source observations; 578 muted and 78 virtual observations among all top-source entries (overlapping populations; not unique source counts).
- In 1,114 source entries: path distribution analytic-box-coverage 394, corner 265, unmeasured 116, direct-through 106, start/clear 74, portal 64, direct-clear 34, direct-partial 21, and other 40.
- Large target gain changes with both endpoints >= 0.02: >=6dB 1,031, >=10dB 634, >=20dB 172, >=30dB 23. These are per-source target changes; NOT proven audible jumps or independently identified emitters. Smoothing, muted/virtual state, changing construction and unmeasured final Unity output all matter.

## Confirmed exception - investigate first
- In latest session log near 18:10:26: Unity ArgumentOutOfRangeException in Heightmap.GetWorldHeight -> Heightmap.GetHeight -> WiresAcoustics.Occlusion.Ridge -> Batch.Ray -> Batch.DirectWalls -> Batch.Apply -> Batch.Step -> Room.Tick.Postfix -> AudioMan.Update.
- Occlusion.Ridge uses 15 calls to Heightmap.GetHeight without a local exception guard, if b.Terrain true. Likely needs safe behavior for an unavailable/transient heightmap; exact engine/root cause not established. Do NOT assume Acoustics authored Unity Heightmap bug; Acoustics invokes the failing call.

## Performance readings (49 3-second-ish report windows)
- Internal audio-thread busy fraction: median 33.6%, sample mean 31.655%, maximum 42.9% (NOT equivalent to a standard Unity main-thread profiler measurement and no baseline recorded).
- Mean per-filter audio callback ~84.57 microseconds across windows; max individual callback in session 22.618ms; also 17.677ms and 16.029ms outliers. Most window maxima ~0.3-0.4ms.
- Reported worst room-tick time: median per-window maximum ~30.047ms, extreme 515.079ms and 193.504ms; no GC marker for those two cases. BUT Room.Tick.Postfix includes Run()->WriteLog()->AcousticTrace.Emit() and synchronous logging of up to 128 transitions + 64 geometry + top 24 sources, without separate cost accounting. Debug logging can severely contaminate these tick maxima. Do NOT claim actual debug-off WA runtime spike of 515ms.
- Saturated physics-hit buffer observations: median 347/window, max 2,775/window. Occlusion.cs explicitly counts a full hit buffer; it does not fabricate additional walls when full. This indicates collision/coverage truncation exposure requiring validation, not definitive audible defect.
- World-generation mismatch in 184 spatial source observations and path age >2s in 82 spatial observations; changes elsewhere in world and intentionally cached paths may explain some. Need check specific live source relevance, not call all stale states bugs.
- 31 selected top-source entries had target-vs-actual gain difference >35% (30 spatial), expected during DSP smoothing but should be correlated with output when testing.

## Trace selection weakness
- AcousticTrace.Emit() ranks source volume * Budget.Open() * target gain, but does not explicitly filter source.mute or source.isVirtual.
- 578 of 1,114 selected logged source entries are muted, and 78 are virtual; the current top-24 view can waste slots on sources that are inaudible in final mixer and miss the relevant audible source. These are repeated snapshots, not 578 unique sounds.

## Interpretation / next gates
1. Address/reproduce terrain-height exception defensively before treating 0.7.0 as stable.
2. Investigate large toggling path targets on continuous loops; current log cannot determine heard waveform.
3. Improve debug trace usefulness and loss reporting if further telemetry is requested; prefer selecting truly audible sounds, and decouple logging time from room tick.
4. Get unbiased debug-OFF gameplay profiler p50/p95/p99 inside and outside dense bases, versus baseline. Current log does not establish regression magnitude.
5. Correlate subjective tests (wall movement, multiwall/terrain, rain roof/partial building/cave, source placement) after user supplies their separate notes.
Status: development-only 0.7.0; no release, code changes, or perceptual acceptance claimed.
