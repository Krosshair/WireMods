@echo off
rem README and changelog back to before the README rewrite.
copy /y "%~dp0README.md" "%~dp0..\..\package\README.md"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
pause
