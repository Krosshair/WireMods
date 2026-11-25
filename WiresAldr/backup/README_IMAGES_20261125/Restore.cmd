@echo off
rem Aldr README and Nexus text back to before the images.
copy /y "%~dp0README.md" "%~dp0..\..\package\README.md"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\NEXUS_DESCRIPTION.txt"
pause
