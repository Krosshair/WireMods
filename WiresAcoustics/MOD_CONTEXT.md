# Wire's Acoustics: context

0.8.1 ready to publish (2026-11-26; 0.8.0 released 2026-11-25). Ray traced sound: reverb from the real room, muffling by material, sound through openings, distance, air, ground, wind, HRTF.

## Architecture
- `Filter.cs` (AcousticFilter): per-sound OnAudioFilterRead on the audio thread: gain + 2-stage low-pass (occlusion, global), air FIR, ground dip, then `Spatialize` (HRTF) on stereo output only. Budget switches the callback's work on/off (a flag, never component enable: that stalls Unity's mixer).
- Air: 16-tap min-phase FIR per 2 m (ISO 9613-1 curve; one-metre cepstrum scaled by distance), built on a worker per weather and output rate. Taps per distance (4 to 20 m warm, 8 to 80 m; within 0.1 dB of 16). Within 0.5 dB of ISO to 150 m. Vector4. `AirCutoff` stays only for estimates and the debug log.
- Ground effect: ISO 9613-2 table 3, relative to the sound's min distance; G from the ground under you (Room.GroundPorous: Nord2000 class D or softer), ground taken flat at your floor. One peaking dip (Q 0.7, mean 0.65 dB off the bands). Set 4 Hz in `Directions.Ground`, follows Realistic distance.
- `Ears.cs`: HRTF. Table `src/ku100.bin` (embedded, "KU4F" + raw deflate): 24 heights x 72 azimuths x 2 ears, 64-tap min-phase filters + per-ear delay. Bilinear blend of 4 directions, 8-sample filter glide, Kaiser-sinc fractional delay. Other sample rates recomputed on a worker.
- Table source: SADIE II humans H3-H20 averaged by ChatGPT (`tools/ku100/human_average.bin`, `human_average_prompt.txt`), `tools/ku100/human.js` keeps the broad colour (2-octave Gaussian of both ears' power sum).
- Room (Room.cs): 64 rays. Reverb length by Eyring with mean free path 4V/S (S from ray distance / hit cos, cos held at .2), half room size, "lived-in" absorption. Reverb tone is the by-ear curve (0.1.2).
- Distance: inverse-distance from the game's min distance; past its range 6 dB more per doubling (fires exempt). Wind: FHWA tables.
- Batch.cs: sound checks queued; Tidy sorts one double key per request through an index array (sorting the big Req structs was most of the main-thread time). The queue held 800-2000 pieces at a base, mostly stale probe pieces (a pass restarted with Version++, old pieces still queued and cast, results dropped in ReadProbe): Tidy now drops them. Budget.Open cached per sound per frame, dead filters skipped by a flag. WiresProfiler: 1.21 ms -> 0.47-0.62 ms before those two; not re-captured since. Tidy's own time counts against Frame.Over (0.5 ms), so a long queue also slowed casting.

## Why it is like this / tried and dropped
- HRTF: picked model (0.7.3) -> Brown & Duda (unreleased) -> smoothed SADIE II average (0.8.0). Dropped: raw average (unclear), medoid single person (sounds ahead heard from behind: +5 dB at 12.5-16 kHz ahead, Blauert's "behind" band), flat straight-ahead EQ.
- Reverb: he tried and rejected a sourced reverb tone (materials per octave) and directional early reflections (image sources through shared HRTF lines). Both in `_removed/session_20261126`. Don't re-propose without a new reason. He dislikes big multi-feature builds: propose small, let him pick.
- Air as a low-pass refit to ISO: over-cut 4-8 kHz far away; a filter shape was needed.
- Camera tilt counts as head tilt (full camera rotation for the HRTF). Yaw-only was discussed; research doesn't settle it, not built.
- Direction chain verified correct (listener has zero local rotation under the camera, Ears.Blend math, table not flipped).

## Workflow quirks
- Filter, Ears, Directions, Occlusion, Room, Batch are LF; check others for CRLF before editing (edit with a script, assert one match).
- Cost in Valheim's Mono: net8 host P/Invoking `MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll` (EmbedRuntime dlls next to the host exe), assemblies path `valheim_Data\Managed`, runs a net472 test exe. Lives in the scratchpad, rebuild it when needed.
- Main-thread cost: his WiresProfiler captures in `BepInEx\WiresProfiler\capture_*.txt` (Gale profile). Read them yourself.
- Check the Gale DLL hash before trusting what he heard (Gale held a pre-0.8.0 DLL until 2026-11-26).
- README sources + "judgement values" lists must stay current.
- Printf in bash eats `\v` / `\E` in Windows paths: write .cmd files with the Write tool.

## Next steps
- Publish 0.8.1 (he runs it). Then re-capture WiresProfiler at the Black Forest base: Acoustics was 1.21 ms main thread before the Tidy fix.
- Not built: forest reverb (Koli, Shelley et al. DAFx 2013: RT 1.3-1.7 s at 1 kHz, dry; OpenAIR impulse responses behind an expired certificate).
- Possible later: ambience through rain's energy model (his "own update").
