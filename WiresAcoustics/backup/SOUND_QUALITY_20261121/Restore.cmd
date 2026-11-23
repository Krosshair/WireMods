@echo off
setlocal
set "ROOT=%~dp0..\.."
set "LIVE=%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll"
tasklist /FI "IMAGENAME eq valheim.exe" | findstr /I /C:"valheim.exe" >nul
if not errorlevel 1 (
 echo ERROR: Close Valheim first.
 exit /b 1
)
for %%F in ("%~dp0src\*.cs") do (
 copy /y "%%~fF" "%ROOT%\src\%%~nxF" >nul
 if errorlevel 1 exit /b 2
)
copy /y "%~dp0WiresAcoustics.dll" "%ROOT%\package\WiresAcoustics.dll" >nul
copy /y "%~dp0CHANGELOG.md" "%ROOT%\package\CHANGELOG.md" >nul
copy /y "%~dp0README.md" "%ROOT%\package\README.md" >nul
copy /y "%~dp0manifest.json" "%ROOT%\package\manifest.json" >nul
copy /y "%~dp0installed_before.dll" "%LIVE%" >nul
if errorlevel 1 exit /b 3
echo Previous sound-quality settings restored.
exit /b 0
