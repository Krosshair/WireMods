@echo off
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first.& exit /b 1)
copy /y "%~dp0before.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresShadows\WiresShadows.dll"
copy /y "%~dp0before_package.dll" "%~dp0..\..\package\WiresShadows.dll"
echo Restored.
pause
