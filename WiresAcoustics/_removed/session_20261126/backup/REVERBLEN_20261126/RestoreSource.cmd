@echo off
rem Acoustics source back to before Eyring reverb length and the ground effect (Room.cs, Filter.cs, Directions.cs) and the context file.
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
copy /y "%~dp0MOD_CONTEXT.md" "%~dp0..\..\"
pause
