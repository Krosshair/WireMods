@echo off
rem Publish.ps1 back to before the Nexus name length fix.
copy /y "%~dp0Publish.ps1" "%~dp0..\..\Publish.ps1"
pause
