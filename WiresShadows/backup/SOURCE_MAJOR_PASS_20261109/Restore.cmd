@echo off
set "SRC=C:\Users\kajet\WireMods\WiresPointlightRework\src_base"
set "DLL=C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresLightOcclusion\WiresLightOcclusion.dll"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first. & exit /b 1)
copy /Y "%~dp0before.dll" "%DLL%" || exit /b 1
echo Restored previous installed DLL. Source files NOT changed.
