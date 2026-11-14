@echo off
copy /Y "%~dp0src\*" "%~dp0..\..\src\"
echo Restored source.
pause
