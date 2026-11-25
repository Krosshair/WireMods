@echo off
rem Aldr source back to before the star colours. Files the update added are moved aside, not deleted.
if not exist "%~dp0..\..\_removed\STARCOLOURS_20261125" mkdir "%~dp0..\..\_removed\STARCOLOURS_20261125"
for %%f in (StarColours RagdollSize) do if exist "%~dp0..\..\src\%%f.cs" move /y "%~dp0..\..\src\%%f.cs" "%~dp0..\..\_removed\STARCOLOURS_20261125\"
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
pause
