@echo off
rem Enemy HUD source back to before the code cleanup. Files the cleanup added are moved aside, not deleted.
if not exist "%~dp0..\..\_removed\CLEANUP_20261125" mkdir "%~dp0..\..\_removed\CLEANUP_20261125"
for %%f in (Nameplate Stars Hover Names Health Stagger StaggerSync Debug StaggerCountdown) do if exist "%~dp0..\..\src\EnemyHud%%f.cs" move /y "%~dp0..\..\src\EnemyHud%%f.cs" "%~dp0..\..\_removed\CLEANUP_20261125\"
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
pause
