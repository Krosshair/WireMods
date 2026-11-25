@echo off
rem Aldr DLL and package back to before 1.4.0 (Gale Enngard profile and package). Close Valheim first.
rem The config file as it was is wire.valheim.heimvoxtr.cfg next to this file.
copy /y "%~dp0gale.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAldr\WiresAldr.dll"
copy /y "%~dp0wire.valheim.heimvoxtr.cfg" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\config\wire.valheim.heimvoxtr.cfg"
copy /y "%~dp0package\*.*" "%~dp0..\..\package\"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\NEXUS_DESCRIPTION.txt"
pause
