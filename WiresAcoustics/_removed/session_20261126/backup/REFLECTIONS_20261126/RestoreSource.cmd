@echo off
rem Acoustics source back to before the directional reflections. Reflections.cs is new: moved to _removed.
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
if not exist "%~dp0..\..\_removed" mkdir "%~dp0..\..\_removed"
if exist "%~dp0..\..\src\Reflections.cs" move /y "%~dp0..\..\src\Reflections.cs" "%~dp0..\..\_removed\Reflections.cs"
copy /y "%~dp0MOD_CONTEXT.md" "%~dp0..\..\"
pause
