@echo off
rem Source back to before water muffling was removed (Underwater.cs included).
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
pause
