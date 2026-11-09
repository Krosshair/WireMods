@echo off
set "SRC=C:\Users\kajet\WireMods\WiresBalancedCraftFromNearbyChests\src"
copy /Y "%~dp0Plugin.cs" "%SRC%\Plugin.cs" || exit /b 1
copy /Y "%~dp0ConnectionVisuals.cs" "%SRC%\ConnectionVisuals.cs" || exit /b 1
if exist "%SRC%\NativeHoverEvents.cs" del "%SRC%\NativeHoverEvents.cs"
echo Previous source restored; rebuild separately if needed.
