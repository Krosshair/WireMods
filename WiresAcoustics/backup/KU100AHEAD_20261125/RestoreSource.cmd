@echo off
rem Acoustics source back to before the neutral straight ahead (0.8.0).
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
copy /y "%~dp0tools\*.*" "%~dp0..\..\tools\ku100\"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\"
pause
