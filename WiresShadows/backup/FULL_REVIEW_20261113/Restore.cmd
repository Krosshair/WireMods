@echo off
copy /y "%~dp0before.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresShadows\WiresShadows.dll"
copy /y "%~dp0before.dll" "%~dp0..\..\package\WiresShadows.dll"
echo Restored. Close Valheim first.
pause
