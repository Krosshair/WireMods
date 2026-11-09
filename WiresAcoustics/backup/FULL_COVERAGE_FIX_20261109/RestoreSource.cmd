@echo off
set "SRC=C:\Users\kajet\WireMods\WiresAcoustics\src"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim before restoring. & exit /b 1)
copy /Y "%~dp0Occlusion.cs" "%SRC%\Occlusion.cs" || exit /b 1
copy /Y "%~dp0Filter.cs" "%SRC%\Filter.cs" || exit /b 1
if exist "%SRC%\AudioProvenance.cs" del "%SRC%\AudioProvenance.cs"
echo Previous source restored. Installed DLL remains unchanged.
