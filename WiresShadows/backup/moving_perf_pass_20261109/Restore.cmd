@echo off
set "DST=C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresLightOcclusion\WiresLightOcclusion.dll"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim before restoring. & exit /b 1)
copy /Y "%~dp0before.dll" "%DST%" || exit /b 1
echo Previous WPR executable restored. Shader bundle untouched.
