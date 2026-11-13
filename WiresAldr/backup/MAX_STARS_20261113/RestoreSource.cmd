@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src\"
echo Restored source. MaxStars.cs stays; move it out by hand if wanted.
pause
