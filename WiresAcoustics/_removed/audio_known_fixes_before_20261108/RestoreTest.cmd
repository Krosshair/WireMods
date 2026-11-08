@echo off
set "LIVE=C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first.& exit /b 1)
copy /Y "%~dp0WiresAcoustics_installed_before.dll" "%LIVE%" || exit /b 1
echo Previous Enngard DLL restored.
