@echo off
rem Nexus-only retry for the 17 Oct 2026 fixes (Nexus mod IDs were missing on the first run).
for %%M in (WiresDecimalsBegone WiresHealthAwareness) do (
  echo.
  call "%~dp0publish.cmd" %%M -SkipBuild -Sites nexus
)
echo.
echo All done. Scroll up for each mods summary.
pause
