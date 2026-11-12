@echo off
set "SRC=C:\Users\kajet\WireMods\WiresPointlightRework\src_base"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim before restoring. & exit /b 1)
copy /Y "%~dp0ShadowCache.cs" "%SRC%\ShadowCache.cs" || exit /b 1
copy /Y "%~dp0Machines.cs" "%SRC%\Machines.cs" || exit /b 1
copy /Y "%~dp0Bench.cs" "%SRC%\Bench.cs" || exit /b 1
copy /Y "%~dp0Plugin.cs" "%SRC%\Plugin.cs" || exit /b 1
echo Previous WPL source restored. Rebuild and deploy separately if needed.
