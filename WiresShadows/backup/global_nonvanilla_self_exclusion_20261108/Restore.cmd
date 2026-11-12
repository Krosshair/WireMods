@echo off
set "P=C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresLightOcclusion"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first. & exit /b 1)
copy /Y "%~dp0WiresLightOcclusion_before.dll" "%P%\WiresLightOcclusion.dll" || exit /b 1
echo Previous working-base WLO test DLL restored; shader bundle unchanged.
