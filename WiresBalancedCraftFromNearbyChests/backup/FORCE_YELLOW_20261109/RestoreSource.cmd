@echo off
set "SRC=C:\Users\kajet\WireMods\WiresBalancedCraftFromNearbyChests\src"
copy /Y "%~dp0ConnectionParticleSafety.cs" "%SRC%\ConnectionParticleSafety.cs" || exit /b 1
copy /Y "%~dp0ConnectionVisuals.cs" "%SRC%\ConnectionVisuals.cs" || exit /b 1
echo Previous source restored. Rebuild DLL separately if needed.
