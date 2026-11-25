@echo off
rem Source back to before automatic language and the second set of names.
copy /y "%~dp0Plugin.cs" "%~dp0..\..\src\Plugin.cs"
pause
