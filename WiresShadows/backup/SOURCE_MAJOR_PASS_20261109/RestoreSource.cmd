@echo off
set "SRC=C:\Users\kajet\WireMods\WiresPointlightRework\src_base"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first. & exit /b 1)
copy /Y "%~dp0Machines.cs" "%SRC%\Machines.cs" || exit /b 1
copy /Y "%~dp0ShadowCache.cs" "%SRC%\ShadowCache.cs" || exit /b 1
copy /Y "%~dp0Bench.before.cs" "%SRC%\Bench.cs" || exit /b 1
if exist "%SRC%\EquipmentEvents.cs" del "%SRC%\EquipmentEvents.cs"
echo Previous source restored. DLL remains unchanged; build and deploy separately if needed.
