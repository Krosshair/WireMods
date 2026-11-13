@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src\"
echo Restored source.
pause
