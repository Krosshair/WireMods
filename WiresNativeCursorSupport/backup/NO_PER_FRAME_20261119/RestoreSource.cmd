@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src\"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
copy /y "%~dp0README.md" "%~dp0..\..\package\README.md"
