@echo off
set "SRC=C:\Users\kajet\WireMods\WiresAcoustics\src"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim before restoring source. & exit /b 1)
copy /Y "%~dp0Ambience.cs" "%SRC%\Ambience.cs" || exit /b 1
copy /Y "%~dp0Filter.cs" "%SRC%\Filter.cs" || exit /b 1
copy /Y "%~dp0Room.cs" "%SRC%\Room.cs" || exit /b 1
copy /Y "%~dp0Travel.cs" "%SRC%\Travel.cs" || exit /b 1
echo Previous acoustics source restored. Rebuild and redeploy separately if necessary.
