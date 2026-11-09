@echo off
set "DST=C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresBalancedCraftFromNearbyChests\WiresBalancedCraftFromNearbyChests.dll"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first. & exit /b 1)
copy /Y "%~dp0before.dll" "%DST%" || exit /b 1
echo Restored old Balanced Craft DLL.
