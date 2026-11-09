@echo off
set "DEST=C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresMeisterhau\WiresMeisterhau.dll"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim before restoring. & exit /b 1)
copy /Y "%~dp0before.dll" "%DEST%" || exit /b 1
echo Previous installed Wire's Meisterhau DLL restored.
