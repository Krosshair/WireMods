@echo off
rem Acoustics DLL and package back to before the raw SADIE II test table. Gale had 0.7.3 installed then. Close Valheim first.
copy /y "%~dp0gale.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll"
copy /y "%~dp0gale_manifest.json" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\manifest.json"
copy /y "%~dp0package\*.*" "%~dp0..\..\package\"
pause
