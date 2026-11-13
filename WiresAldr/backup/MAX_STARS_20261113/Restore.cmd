@echo off
copy /y "%~dp0before_WiresAldr.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresAldr\WiresAldr.dll"
copy /y "%~dp0before_WiresAldr.dll" "%~dp0..\..\package\WiresAldr.dll"
echo Restored. Close Valheim first.
pause
