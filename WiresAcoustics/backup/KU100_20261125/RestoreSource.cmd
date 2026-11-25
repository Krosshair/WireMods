@echo off
rem Acoustics source back to before the KU100 ears (0.8.0). Ears.cs and its table go to _removed here.
if not exist "%~dp0_removed" mkdir "%~dp0_removed"
if exist "%~dp0..\..\src\Ears.cs" move /y "%~dp0..\..\src\Ears.cs" "%~dp0_removed\"
if exist "%~dp0..\..\src\ku100.bin" move /y "%~dp0..\..\src\ku100.bin" "%~dp0_removed\"
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\"
pause
