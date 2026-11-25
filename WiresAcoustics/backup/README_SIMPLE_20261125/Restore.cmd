@echo off
rem Acoustics texts back to before the simple README.
copy /y "%~dp0README.md" "%~dp0..\..\package\README.md"
copy /y "%~dp0manifest.json" "%~dp0..\..\package\manifest.json"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\NEXUS_DESCRIPTION.txt"
pause
