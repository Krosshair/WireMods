# Wire's Acoustics: context

Released 0.8.0 (2026-11-25). Ray traced sound: reverb from the real room, muffling by material, sound through openings, distance, air, wind, HRTF.

## Test build installed (2026-11-26, not released, docs/changelog not updated yet)
- HRTF table = medoid: the one SADIE II person most typical of the 18 (by direction cues), diffuse-field EQ, no smoothing (`tools/ku100/medoid.bin`, prompt `EarsPrompt/MEDOID_PROMPT.txt`). He: "sounds good" but doesn't gel with the rest.
- Air as a 16-tap min-phase FIR per 2 m (ISO 9613-1 curve itself, within 0.5 dB to 150 m), after the muffling (wall + air now add). Replaces air as a low-pass cutoff (that left 12-16 kHz up to 25 dB too bright far away). Cost 2.65 us per channel per 1024 buffer in Valheim's Mono (Vector4).
- Reverb tone sourced (was by ear since 0.1.2): every surface has 500 Hz and 4 kHz absorption (pyroomacoustics, Sengpiel, Delany-Bazley ground per octave, bark flat; iron and props flat, no data). RoomHF = room constant at 4 kHz (+ air) vs 500 Hz; DecayHF = Sabine at 4 kHz (+ air 4mV) / mid; HFReference 4000. "Cozy" -4 dB kept (by ear). Wooden rooms now much brighter (was 12-15 dB darker than data). Reverb level may need a recheck.
- Reverb length: Eyring (was Sabine) with mean free path 4V/S, S from ray distances / hit cos (cos held at .2; 64 rays in box rooms: unbiased, 13% scatter; mean distance was 20% short).
- Ground effect: ISO 9613-2 table 3 (formulas checked in the standard), relative to the sound's min distance, G from ground under you (class D or softer porous), flat ground at your floor height. One peaking dip, Q 0.7 (mean 0.65 dB, max 3.4 dB off the bands), after the air. Set 4 Hz in Directions, follows Realistic distance. Middle-region broadband term (mixed ground, under 1.5 dB) left out.
- Forest reverb: data so far Koli (Shelley et al., DAFx 2013): RT 1.3-1.7 s at 1 kHz, short below 200 Hz, "relatively dry". Level (C50) only in a figure; OpenAIR has the impulse responses.
- Backups: `backup/RAWHUMAN_20261126/src` (smoothed 0.8.0 table), `MEDOID_20261126`, `AIRFIT_20261126` (source before the FIR air), `REVERBTONE_20261126` (before the sourced tone), `REVERBLEN_20261126` (before Eyring + ground).
- When shipping: changelog, README sources/judgement values (HRTF line no longer "2 octaves"), SADIE_LICENSE.txt changes list, NEXUS_DESCRIPTION.

## Architecture
- `Filter.cs` (AcousticFilter): per-sound OnAudioFilterRead on the audio thread: gain + 2-stage low-pass (occlusion, global), air FIR, then `Spatialize` (HRTF) on stereo output only. Budget switches the callback's work on/off (a flag, never component enable: that stalls Unity's mixer).
- `Ears.cs`: HRTF. Table `src/ku100.bin` (embedded resource, "KU4F" + raw deflate, lossless delta packing): 24 heights x 72 azimuths x 2 ears, 64-tap min-phase filters + per-ear delay. Bilinear blend of 4 directions, filters glide in 8-sample steps, delay via 8-point Kaiser-sinc fractional delay, near ear delay 0. Vector4 SIMD (Valheim's Mono accelerates it). Other sample rates: table recomputed on a worker (cepstrum).
- Air FIR tables: built on a worker per weather and rate (one-metre cepstrum scaled by distance). `AirCutoff` stays only for estimates and the debug log.
- Distance: inverse-distance from the game's min distance; past the game's range 6 dB more per doubling (`PastRange`, fires exempt). Occlusion/rays on workers.
- Interaction: the spot you touched counts only within reach (Interaction.cs, ShapeRoot/parent ownership). Ambience muffling uses Room.Closed.

## Why it is like this
- HRTF: picked model (0.7.3) -> Brown & Duda (0.7.4, never released) -> SADIE II average + 2-octave smoothing (0.8.0) -> medoid (test). Raw average sounded unclear to him. The 6-10 kHz dip ahead (-7 to -8 dB) is in the medoid too: a real ear feature at 64-tap detail, not averaging smear.
- Smoothing width (2 octaves) had no source; Merimaa "Modification of HRTF filters to reduce timbral effects" is the closest literature.
- Tried and dropped: flat straight-ahead EQ (Blauert directional bands), plain 6 dB/doubling everywhere ("superman"), refitting the 2-stage air low-pass to ISO (over-cut 4-8 kHz by 4-8 dB far away; `_removed/Filter_airfit_20261126.cs`).
- Outdoor stack measured: distance and wind broadband (sourced), outdoor reverb -26 dB (judgement). Not modelled: ground effect (ISO 9613-2), look up before adding.
- HRTF strength setting is 0..1 (was 0..2).

## Workflow quirks
- Some source files have CRLF: edit with a CRLF-aware script, assert one match (Filter, Ears, Directions, Occlusion are LF).
- Cost in Valheim's Mono: net8 host P/Invoking `mono-2.0-bdwgc.dll` (copy EmbedRuntime dlls next to the host exe), assemblies path `valheim_Data\Managed`, runs a net472 test exe. Scratchpad, lost on reboot.
- Gale's profile had a pre-0.8.0 DLL until 2026-11-26: check the installed DLL hash before trusting what he heard.
- README sources list + "judgement values" list must stay current.

## Next steps
- He hears the medoid + FIR air. If it still doesn't gel: candidates are reverb having no direction (Unity reverb zone) and stronger tone change with turning.
- Possible later: ambience through rain's energy model (his "own update").
