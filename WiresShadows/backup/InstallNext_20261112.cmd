@echo off
set P=%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins
copy /y "%~dp0WiresPointlightRework\stage_next\WiresLightOcclusion.dll" "%P%\Wire-WiresLightOcclusion\WiresLightOcclusion.dll"
copy /y "%~dp0WiresAcoustics\stage_next\WiresAcoustics.dll" "%P%\Wire-WiresAcoustics\WiresAcoustics.dll"
echo Installed. Valheim must be closed.
pause
