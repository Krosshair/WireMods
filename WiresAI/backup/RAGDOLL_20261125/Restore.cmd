@echo off
rem Wire's AI DLL and package back to before the body sizes (Gale Enngard profile and package). Close Valheim first.
copy /y "%~dp0gale.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAI\WiresAI.dll"
copy /y "%~dp0package\*.*" "%~dp0..\..\package\"
pause
