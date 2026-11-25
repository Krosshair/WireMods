@echo off
rem Wire's Acoustics DLL and changelog back to before the R8 fix (Gale Enngard profile and package). Close Valheim first.
copy /y "%~dp0WiresAcoustics.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAcoustics\WiresAcoustics.dll"
copy /y "%~dp0WiresAcoustics.dll" "%~dp0..\..\package\WiresAcoustics.dll"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
pause
