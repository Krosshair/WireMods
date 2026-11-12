@echo off
cd /d "%~dp0"
copy /y src\* ..\..\src\
copy /y NEXUS_DESCRIPTION.txt ..\..\NEXUS_DESCRIPTION.txt
echo Restored source.
pause
