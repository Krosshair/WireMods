# WiresAcoustics: physics-source review and single-edge Fresnel replacement
Date: 2026-11-22
Status: LOCAL DEVELOPMENT TEST BUILD, NOT RELEASED; in-game sound and CPU performance not verified.

## References examined
1. ITU-R P.526-16, diffraction, section 4.1 single knife-edge: https://www.itu.int/dms_pubrec/itu-r/rec/p/R-REC-P.526-16-202511-I!!PDF-E.pdf
2. Fresnel half-plane complex field (normalized C,S representation): https://pmc.ncbi.nlm.nih.gov/articles/PMC4648623/
3. Steam Audio official integration: https://valvesoftware.github.io/steam-audio/doc/unity/guide.html and https://valvesoftware.github.io/steam-audio/doc/unity/source.html
4. Microsoft Project Acoustics: https://github.com/microsoft/ProjectAcoustics
5. Rectangular-aperture diffraction, distinct from single-edge half-plane: https://farside.ph.utexas.edu/teaching/315/Waveshtml/node101.html

## Audit findings, tied to source
- Starts.Schedule already checks each new sound's direct and face rays via RayJobs and starts after asynchronous results. No need to remove or re-create this.
- Batch/Corners use actual batched raycasts to locate openings and return a checked route, but per-sound edge discovery may occur later than the sound's initial direct obstruction test.
- The previous ContinuousEdges.Transfer contained an invented exponential for shadow transmission and arbitrary geometric averaging of up to four edges. Neither operation is the solution of a wave equation or a validated rectangular-aperture propagation model.
- Steam Audio supports frequency-dependent occlusion/transmission, dynamic geometry, baked pathing and real-time path validation. Its precomputed pathing is NOT automatically correct for arbitrary Valheim-generated/dynamic builds. A complete engine substitution would require native dependency, geometry ownership, runtime integration and performance testing; not safe to pretend it is a one-file drop-in.
- Project Acoustics relies on baked precomputed propagation, similarly problematic for unpredictable player buildings.
- Discrete values are legitimate for *geometry existence and job validity*, not for sound gain. The current DSP has only an amplitude and one cutoff, not a full bandwise complex pressure field.

## Changes made
- Replaced invented exponential transfer and arbitrary multiple-edge blending with scalar complex Fresnel diffraction for a verified SINGLE finite edge of a stationary wall.
- Formula: D(v)=[1+C(v)+S(v)]/2 + i[S(v)-C(v)]/2, with C,S the Fresnel integrals, normalized parameter v=clearance*sqrt(2*(d1+d2)*f/(c*d1*d2)).
- Thin uniform transmissive half-plane approximation: U = D + T*(1-D). With T=0 this is opaque-screen diffraction, T=1 perfectly transparent, at the edge D=.5 and the same pressure is approached from either side.
- Lookup table for C,S is integrated numerically once in the class static initializer with Simpson quadrature, sampled/interpolated at runtime. Uses two frequencies (125 Hz and 2 kHz) to map onto the original DSP gain+cutoff. Does not allocate or trigger any jobs within the sound update.
- IMPORTANT: when the verified wall record contains more than one edge (a window, a pillar, a gap), the method deliberately refuses to predict a field, leaving the original Batch solver in control. The old averaging was not a valid wave model.
- Geometry change invalidation, event hooks, main-thread sound management, asynchronous ray jobs and the original fallback checks are unchanged.

## Numeric verification, actual compiled net472 DLL
- Independent reference Fresnel C,S at v=.25,.5,1,2,3,5,8 from SciPy scipy.special.fresnel: within absolute .001.
- Field D(0)=.5+0i; opaque edge intensity .25 => amplitude .5 (6.02 dB loss).
- T=1 => unity intensity for shadow, transition, clear zone.
- Opaque edge: intensity at deep shadow < .01 and fully clear -> unity.
- For T={0,.1,.5,.9,1}, left and right of boundary match to < .0002.
- Parameter sweeps constrain resulting gain and cutoff to the DSP's representable range.
- Build: 0 warnings, 0 errors.
- These tests DO NOT verify acoustics with Valheim geometry, cpu cost, native hook compatibility, reflections or listening. Release remains blocked.

## Known limitations
- The half-plane model is not a complete room acoustic solver; edge diffraction is approximated as a scalar wave field with unknown wall transmission phase.
- The 2-band result is fitted to the existing lowpass+gain DSP; it cannot reproduce spectral combing/peaks or interference without changes to the audio filter.
- The model uses a single validated edge and listener/source distances to its wall plane, and cannot infer windows, holes, multiple corners or diffraction via a corridor.
- Source positioning toward the corner is a perceptual heuristic, not a physically solved direction-of-arrival field.
- Starts still discovers some alternative paths after playback starts; a proper shared geometry index is a separate architectural project.
- Safety refreshes remain for external geometry changes not exposed by managed events, as removing them was a regression.

## Rollback
Close Valheim, run backup\FRESNEL_AUDIT_20261122\Restore.ps1.
