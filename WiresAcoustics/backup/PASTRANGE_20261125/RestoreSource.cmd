@echo off
rem Acoustics source back to before the extra fading past the range (0.8.0).
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\"
pause
