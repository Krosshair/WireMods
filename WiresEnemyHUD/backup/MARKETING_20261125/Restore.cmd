@echo off
rem README, manifest and Nexus text back to before the player-focused rewrite.
copy /y "%~dp0README.md" "%~dp0..\..\package\README.md"
copy /y "%~dp0manifest.json" "%~dp0..\..\package\manifest.json"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\NEXUS_DESCRIPTION.txt"
pause
