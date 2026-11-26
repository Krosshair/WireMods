@echo off
rem Acoustics source back to before the optimisation pass (Batch.cs, Occlusion.cs, Filter.cs) and the context file.
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
copy /y "%~dp0MOD_CONTEXT.md" "%~dp0..\..\"
pause
