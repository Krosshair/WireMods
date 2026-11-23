WA 0.7.0 RC roof continuity 2026-11-23
Purpose: return previous roof coloration and avoid temporary EnvironmentalField.Ready dropouts
Technique: original game rain recording / single AudioSource, no artificial impact sounds.
Real authored nearby roof via existing Room upward rays; floors excluded.
Roof attack 0.42 s / release 1.25 s; exterior field gain and cutoff fallback smoothed 0.38 s.
Targeted regression and static release gate PASSED. net472 build: 0 warnings/errors.
Build / packed / Enngard DLL SHA256: 41BB2AC28664679F00D5D84443CE89B206C45E9CB9FB748B29DB1DF1C3BF83AB
Staged package: C:\Users\kajet\WireMods\WiresAcoustics\staging\WiresAcoustics-0.7.0-RC-roof-continuity-20261123.zip
Zip SHA256: B2B436E97927F4D6056A65BA033779114E8882174F9078881AFDD1C09BDD1F7F
Rollback (Valheim closed): C:\Users\kajet\WireMods\WiresAcoustics\backup\RAIN_ROOF_READY_CONTINUITY_20261123\installed.dll
In-game ear testing and profiler: NOT performed.
