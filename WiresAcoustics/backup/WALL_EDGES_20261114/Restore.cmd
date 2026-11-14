@echo off
copy /y "%~dp0before.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll"
copy /y "%~dp0before.dll" "%~dp0..\..\package\WiresAcoustics.dll"
echo Restored. Close Valheim first.
pause
