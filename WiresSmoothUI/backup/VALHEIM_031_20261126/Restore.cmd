@echo off
rem Wire's Smooth UI back to 0.6.11. Close Valheim first.
copy /y "%~dp0gale.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresSmoothUI\WiresSmoothUI.dll"
copy /y "%~dp0package\*.*" "%~dp0..\..\package\"
pause
