@echo off
rem Source back to before the measured open panels. The new shader and its build menu in LightRework are left; they do nothing on their own.
robocopy "%~dp0src" "%~dp0..\..\src" /E /XD bin obj
echo Source restored.
pause
