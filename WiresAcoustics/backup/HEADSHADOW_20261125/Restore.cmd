@echo off
rem Acoustics DLL and package back to before the head model (0.7.4). Close Valheim first.
copy /y "%~dp0gale.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll"
copy /y "%~dp0package\*.*" "%~dp0..\..\package\"
pause
