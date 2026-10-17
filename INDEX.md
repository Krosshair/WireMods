# Wire's mods: source of truth

Created 17 Oct 2026. Each folder: `src/` = the exact source of the shipped/installed DLL (matched by SHA256 against bin output), `package/` = the release files (DLL, manifest, README, icon...).
Publishing: see PUBLISHING.md (`setup.cmd` once, then `publish <Mod> -Version x.y.z`).
Build: `dotnet build -c Release` inside `src/`. Paths default to this PC; override with -p:BepInExCore=... -p:ValheimManaged=...

| Mod | Manifest | Origin of src | State |
|---|---|---|---|
| WiresAldr | 0.3.2 | WiresAldr041Fixes/src | installed |
| WiresAltBiomeInfo | 0.1.6 | WiresBiomeSubtypeDisplayBuild/src | installed |
| WiresBalancedCraftFromNearbyChests | 0.1.8 | WiresBalancedCraftFromNearbyChestsBuild/src | installed |
| WiresCameraTweaks | 0.1.0 | WiresCameraOverhaulBuild/src | installed |
| WiresComfortInfo | 0.1.1 | WiresComfortInfoBuild/src | installed |
| WiresDecimalsBegone | 0.1.1 | WiresDecimalsBegoneBuild/src | installed |
| WiresEnemyHUD | 0.6.0 | EnemyHud060HitVisibility/src | installed |
| WiresForgeOfGoodtential | 0.1.15 | WiresForgeOfGoodtentialBuild/src | installed |
| WiresGrassTweaks | 0.2.301 | WiresGrassTweaks023Recovered/src | installed |
| WiresHealthAwareness | 0.3.7 | WiresHealthVignettingBuild/src | installed |
| WiresHelmetHider | 0.2.4 | WiresHoodToggleBuild/src | installed |
| WiresInstafill | 1.0.1 | WiresInstafillBuild/src | installed |
| WiresLegacyRaids | 0.1.1 | WiresLegacyRaidsBuild/src | installed |
| WiresMeisterhau | 0.2.4 | WiresMeisterhauBuild/src | installed |
| WiresNativeCursorSupport | 0.3.2 | WiresNativeCursorSupportBuild/src_v2 | installed |
| WiresNotificationOverhaul | 1.1.6 | Patches_20261017/NotificationOverhaul | installed |
| WiresRecycler | 1.0.2 | WiresRecyclerBuild/src | installed |
| WiresSailingCamera | 0.2.2 | WiresSailingCameraBuild/src | installed |
| WiresSmoothUI | 0.5.4 | SmoothUI054HealthFade/src | installed |
| WiresStableFirelight | 1.0.0 | StableFirelightBuild/src | installed |
| WiresTinyToolTweaks | 1.0.2 | WiresTinyToolTweaksBuild/src | installed |
| WiresWeaponMaster | 0.2.1 | WiresWeaponMasterBuild/src | installed |
| WiresWeightedSlots | 0.1.1 | WiresWeightedSlotsBuild/src | installed |
| WiresCombatFlow | 2.1.0 | WiresCombatFlowBuild/src | not installed |
| WiresPings | 0.1.2 | WiresPingsBuild/src | not installed |
| WiresStandSwap | 0.3.1 | WiresStandSwap031/src | not installed |

Notes:
- WiresAldr: DLL is 0.4.1 while manifest stays 0.3.2 on purpose.
- WiresStandSwap: src is the newer local 0.3.1; Thunderstore has 0.2.0 (source: WiresStandSwapBuild/src).
- WiresGrassTweaks: original source was lost; src is a cleaned rewrite of the decompiled 0.2.3 DLL.

## Changes made 17 Oct 2026 (built, installed, NOT yet tested in game)
- WiresAldr 0.4.1 (DLL; manifest stays 0.3.2): see WiresAldr/RELEASE_NOTES_0.4.1.txt.
- WiresBalancedCraftFromNearbyChests 0.1.8: removed leftover "99 - Temporary Diagnostics" log audit (global UnityEngine.Debug.Log patch + particle scans).
- WiresRecycler 1.0.2: crafting-XP debt is saved in the character's vanilla custom data, so death/relog no longer clears it.
- WiresNotificationOverhaul 1.1.6: ServerSync and the "Lock Configuration" setting removed; every setting is personal/client-side now.
- WiresDecimalsBegone 0.1.1: numbers below 1 keep their decimal; 1 and above are rounded.
- WiresHealthAwareness 0.3.7: camera component cached instead of looked up every frame.
- WiresLegacyRaids 0.1.1: works with player-based raids (clients with the mod report their retired-by-boss raids; server needs it too).
- WiresGrassTweaks: readable source rebuilt from the shipped 0.2.3 DLL (same behavior). Installed DLL left unchanged.
- WiresMeisterhau: csproj pointed at a Valheim install under C:\Program Files; now uses the D: game folder like the rest.
