@echo off
rem Source back to before the name changes.
copy /y "%~dp0Plugin.cs" "%~dp0..\..\src\Plugin.cs"
pause
