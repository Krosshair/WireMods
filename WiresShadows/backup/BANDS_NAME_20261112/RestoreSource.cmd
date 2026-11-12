@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src_base\"
echo Restored source.
pause
