@echo off
setlocal
set "ROOT=%~dp0..\.."
set "LIVE=%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll"
set "CONFIG=%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\config\wire.valheim.acoustics.cfg"
tasklist /FI "IMAGENAME eq valheim.exe" | findstr /I /C:"valheim.exe" >nul
if not errorlevel 1 (
 echo Close Valheim before reverting.
 exit /b 1
)
for %%F in ("%~dp0src\*.cs") do (
 copy /y "%%~fF" "%ROOT%\src\%%~nxF" >nul
 if errorlevel 1 exit /b 2
)
del /q "%ROOT%\src\RayJobs.cs" 2>nul
del /q "%ROOT%\src\AcousticMath.cs" 2>nul
copy /y "%~dp0package\WiresAcoustics.dll" "%ROOT%\package\WiresAcoustics.dll" >nul
copy /y "%~dp0package\CHANGELOG.md" "%ROOT%\package\CHANGELOG.md" >nul
copy /y "%~dp0package\README.md" "%ROOT%\package\README.md" >nul
copy /y "%~dp0package\manifest.json" "%ROOT%\package\manifest.json" >nul
copy /y "%~dp0installed_before.dll" "%LIVE%" >nul
if errorlevel 1 exit /b 3
copy /y "%~dp0config_before.cfg" "%CONFIG%" >nul
if errorlevel 1 exit /b 4
echo Published 0.6.0 pre-refactor source, DLL and settings restored.
exit /b 0
