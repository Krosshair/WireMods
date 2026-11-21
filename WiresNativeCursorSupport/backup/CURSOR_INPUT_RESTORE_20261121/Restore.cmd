@echo off
setlocal
set "ROOT=C:\Users\kajet\WireMods\WiresNativeCursorSupport"
set "BK=%ROOT%\backup\CURSOR_INPUT_RESTORE_20261121"
set "GAME=C:\Users\kajet\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresNativeCursorSupport"
tasklist /FI "IMAGENAME eq valheim.exe" | find /I "valheim.exe" >nul && (echo Close Valheim first. & exit /b 1)
copy /Y "%BK%\installed_before.dll" "%GAME%\WiresNativeCursorSupport.dll.new" >nul || exit /b 1
move /Y "%GAME%\WiresNativeCursorSupport.dll.new" "%GAME%\WiresNativeCursorSupport.dll" >nul || exit /b 1
copy /Y "%BK%\package_before.dll" "%ROOT%\package\WiresNativeCursorSupport.dll.new" >nul || exit /b 1
move /Y "%ROOT%\package\WiresNativeCursorSupport.dll.new" "%ROOT%\package\WiresNativeCursorSupport.dll" >nul || exit /b 1
echo Previous DLL restored.
