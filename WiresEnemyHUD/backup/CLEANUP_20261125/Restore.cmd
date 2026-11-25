@echo off
rem Enemy HUD DLL and package texts back to before the code cleanup (Gale Enngard profile and package). Close Valheim first.
copy /y "%~dp0gale.dll" "%APPDATA%\com.kesomannen.gale\valheim\profiles\Enngard\BepInEx\plugins\Wire-WiresEnemyHUD\EnemyHudRework.dll"
copy /y "%~dp0package\*.*" "%~dp0..\..\package\"
pause
