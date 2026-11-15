@echo off
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first. & exit /b 1)
copy /Y "%~dp0before.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresStackify\WiresStackify.dll"
copy /Y "%~dp0before.dll" "%~dp0..\..\package\WiresStackify.dll"
copy /Y "%~dp0src\*" "%~dp0..\..\src\"
if exist "%~dp0..\..\src\MouseControls.cs" move /y "%~dp0..\..\src\MouseControls.cs" "%~dp0MouseControls.removed.cs"
echo Restored.
pause
