@echo off
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim before restoring. & exit /b 1)
set "R=C:\Users\kajet\WireMods\WiresStackify"
copy /Y "%~dp0package_before.dll" "%R%\package\WiresStackify.dll" || exit /b 1
copy /Y "%~dp0installed_before.dll" "C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresStackify\WiresStackify.dll" || exit /b 1
echo Prior package and installed DLLs restored.
