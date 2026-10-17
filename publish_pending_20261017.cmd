@echo off
rem Publishes the 17 Oct 2026 fixes. Uses the exact DLLs already installed in your Gale profile (-SkipBuild).
rem Sites that already have a version are skipped automatically.
for %%M in (WiresDecimalsBegone WiresHealthAwareness WiresNotificationOverhaul WiresLegacyRaids WiresRecycler WiresBalancedCraftFromNearbyChests) do (
  echo.
  call "%~dp0publish.cmd" %%M -SkipBuild
)
echo.
echo All done. Scroll up for each mods summary.
pause
