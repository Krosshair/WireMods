@echo off
rem Alt Biome Info back to before the name changes (Gale Enngard profile and package). Close Valheim first.
copy /y "%~dp0gale\*.*" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAltBiomeInfo\"
copy /y "%~dp0package\*.*" "%~dp0..\..\package\"
pause
