@echo off
copy /y "%~dp0before.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAI\WiresAI.dll"
copy /y "%~dp0before_package.dll" "%~dp0..\..\package\WiresAI.dll"
echo Restored. Close Valheim first.
pause
