@echo off
set "PROFILE=C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first.& exit /b 1)
copy /Y "%~dp0live_before.dll" "%PROFILE%\plugins\Wire-WiresLightOcclusion\WiresLightOcclusion.dll" || exit /b 1
copy /Y "%~dp0config_before.cfg" "%PROFILE%\config\wire.valheim.lightocclusion.cfg" || exit /b 1
echo Previous installed WLO DLL and config restored.
