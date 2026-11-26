# Wire's Acoustics: context

Released 0.8.0 (2026-11-25). Ray traced sound: reverb from the real room, muffling by material, sound through openings, distance, air, wind, HRTF.

## Architecture
- `Filter.cs` (AcousticFilter): per-sound OnAudioFilterRead on the audio thread: gain + 2-stage low-pass (occlusion, air, distance), then `Spatialize` (HRTF) on stereo output only. Budget switches the callback's work on/off (a flag, never component enable: that stalls Unity's mixer).
- `Ears.cs`: HRTF. Table `src/ku100.bin` (embedded resource, "KU4F" + raw deflate, lossless delta packing): 24 heights x 72 azimuths x 2 ears, 64-tap min-phase filters + per-ear delay. Bilinear blend of 4 directions, filters glide in 8-sample steps, delay via 8-point Kaiser-sinc fractional delay, near ear delay 0. Vector4 SIMD (Valheim's Mono accelerates it). Other sample rates: table recomputed on a worker (cepstrum).
- Table source: average of SADIE II humans H3-H20, made by ChatGPT (`tools/ku100/human_average.bin`, prompt in `human_average_prompt.txt`), then `tools/ku100/human.js` keeps only the broad colour (2-octave Gaussian of both ears' power sum, same curve on both ears). License: `package/SADIE_LICENSE.txt` (Apache 2.0, changes listed).
- Distance: inverse-distance from the game's min distance; past the game's range 6 dB more per doubling (`PastRange`, fires exempt). Air: ISO 9613-1. Occlusion/rays on workers.
- Interaction: the spot you touched counts only within reach (Interaction.cs, ShapeRoot/parent ownership). Ambience muffling uses Room.Closed.

## Why it is like this
- HRTF was a picked model (0.7.3) -> Brown & Duda (0.7.4, never released) -> measured. KU100 dummy raw sounded like "bad earphones" (direction-dependent peaks/dips, -8.6 dB at 8 kHz ahead). Broad-colour smoothing fixed that; human average gives stronger up/down.
- Tried and dropped: flat straight-ahead EQ (absolute frequency cues matter: Blauert directional bands), plain 6 dB/doubling everywhere (he heard everything: "superman").
- Measured (real code): ILD within 0.15 dB of the average, ITD ~0.5 us, cost 27-49 us per sound per 1024 buffer (same as old model). Front/back 2.52 dB, up/down 2.65 dB (0.7.4: 1.84 / 1.02).
- HRTF strength setting is 0..1 (was 0..2).

## Workflow quirks
- Source files have mixed CRLF: edit with a CRLF-aware script, assert one match.
- Verify table changes with the eartest harness (scratchpad, lost on reboot): impulse responses vs recordings, tone per direction, cost in Valheim's own Mono (net8 host loading `MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll`, needs EmbedRuntime on PATH for DeflateStream).
- README sources list + "judgement values" list must stay current (HRTF 2 octaves, distance past range, etc.).

## Next steps
- He has only tested 0.8.0 in the plains. Watch for reports of deeper/duller tone.
- Possible later: plain human average as an option; ambience through rain's energy model (his "own update").
