@echo off
rem Acoustics source back to before the Budget.Open cache (Batch.cs, Budget.cs, Filter.cs).
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
pause
