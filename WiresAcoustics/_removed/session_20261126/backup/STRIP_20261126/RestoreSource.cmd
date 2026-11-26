@echo off
rem Acoustics source back to the full build (reverb tone and reflections included, Reflections.cs back from here).
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
copy /y "%~dp0MOD_CONTEXT.md" "%~dp0..\..\"
pause
