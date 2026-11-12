@echo off
set P=%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresLightOcclusion
copy /y "%~dp0before.dll" "%P%\WiresLightOcclusion.dll"
echo Restored DLL. Close Valheim first.
pause
