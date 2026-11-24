@echo off
rem Wire's Acoustics back to before the measured open panels (Gale Enngard profile and package).
copy /y "%~dp0WiresAcoustics.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll"
copy /y "%~dp0WiresAcoustics.dll" "%~dp0..\..\package\WiresAcoustics.dll"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
echo Restored. Close Valheim first.
pause
