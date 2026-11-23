WA 0.7.0 RC menu-audio source ownership correction 2026-11-23
Bug: Menu.Postfix(FejdStartup.Start) formerly global-searched all AudioSources and reclassified persistent AudioMan ambient/ocean/old-world objects into Kinds.Menu. The original code also attached filters to sources with no clip. These are source-confirmed defects; not every menu warning is thereby explained.
Change: use FejdStartup's own scene, require a playable clip and a spatial source, retain current owner for ambient/ocean/outside/voice, HRTF for menu-owned sources. FejdStartup.Start diagnostic records state of Amb_MainMenu.
Important: The Unity warning occurs before FejdStartup.Start, so this menu-hook fix must NOT be represented as proven to remove that early warning. If warning persists, check [acoustics-menu] diagnostic and investigate native playOnAwake/WA AudioSettings.Reset interaction.
Build 0 errors / 0 warnings. Full dev\VerifyEveryImplementation.ps1 PASS.
Source and installed DLL SHA256: 8B3FE34F6A223B62038EDF59750F9661335F46805ED63899A753E4C7EB0460E0
Backup: C:\Users\kajet\WireMods\WiresAcoustics\backup\WA_070_MENU_SCENE_20261123\WiresAcoustics.dll
Staging zip: C:\Users\kajet\WireMods\WiresAcoustics\staging\WiresAcoustics-0.7.0-RC-menu-ownership-20261123.zip
Post-change gameplay/log testing NOT performed. No public release.