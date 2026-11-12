@echo off
cd /d "%~dp0"
copy /y package\* ..\..\package\
echo Restored package.
pause
