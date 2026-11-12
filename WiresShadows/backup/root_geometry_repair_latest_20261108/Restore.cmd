@echo off
set "PROFILE=C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first.& exit /b 1)
copy /Y "%~dp0before.dll" "%PROFILE%\plugins\Wire-WiresLightOcclusion\WiresLightOcclusion.dll" || exit /b 1
copy /Y "%~dp0before.cfg" "%PROFILE%\config\wire.valheim.lightocclusion.cfg" || exit /b 1
echo Restored previously installed DLL and configuration.
