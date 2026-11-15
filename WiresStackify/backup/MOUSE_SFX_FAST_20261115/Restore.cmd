@echo off
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first & exit /b 1)
copy /Y "%~dp0stackify_before.dll" "C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresStackify\WiresStackify.dll" || exit /b 1
copy /Y "%~dp0smoothui_before.dll" "C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresSmoothUI\WiresSmoothUI.dll" || exit /b 1
echo Previous Stackify and Smooth UI DLLs restored together.
