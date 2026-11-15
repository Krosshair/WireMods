@echo off
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first & exit /b 1)
copy /Y "%~dp0before.dll" "C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresStackify\WiresStackify.dll" || exit /b 1
copy /Y "C:\Users\kajet\WireMods\WiresSmoothUI\backup\STACKIFY_FAST_TRANSFER_20261115\before.dll" "C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresSmoothUI\WiresSmoothUI.dll" || exit /b 1
echo Previous Stackify AND Smooth UI DLLs restored.
