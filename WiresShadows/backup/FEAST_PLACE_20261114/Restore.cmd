@echo off
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first. & exit /b 1)
copy /Y "%~dp0WiresShadows.dll" "C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresShadows\WiresShadows.dll"
copy /Y "%~dp0WiresShadows.dll" "%~dp0..\..\package\WiresShadows.dll"
echo Restored DLL.
pause
