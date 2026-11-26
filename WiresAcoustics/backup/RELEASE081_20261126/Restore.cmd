@echo off
rem Acoustics DLL and package back to the 0.8.0 release. Close Valheim first.
copy /y "%~dp0package\WiresAcoustics.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll"
copy /y "%~dp0package\*.*" "%~dp0..\..\package\"
pause
