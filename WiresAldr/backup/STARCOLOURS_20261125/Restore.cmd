@echo off
rem Aldr DLL and package back to before the star colours and ragdoll sizes (1.4.0 build) (Gale Enngard profile and package). Close Valheim first.
copy /y "%~dp0gale.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAldr\WiresAldr.dll"
copy /y "%~dp0package\*.*" "%~dp0..\..\package\"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\NEXUS_DESCRIPTION.txt"
pause
