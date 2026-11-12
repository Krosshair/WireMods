@echo off
set "PROFILE=C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo CLOSE VALHEIM FIRST & exit /b 1)
copy /Y "%~dp0KNOWN_WORKING_base.dll" "%PROFILE%\plugins\Wire-WiresLightOcclusion\WiresLightOcclusion.dll" || exit /b 1
echo Restored EXACT Nov 7 known-working base DLL. Matching existing wirelightocclusion shader bundle left untouched.
