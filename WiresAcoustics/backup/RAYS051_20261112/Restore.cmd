@echo off
cd /d "%~dp0"
copy /y package\* ..\..\package\
set P=%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics
copy /y package\WiresAcoustics.dll "%P%\WiresAcoustics.dll"
echo Restored package and installed DLL. Close Valheim first.
pause
