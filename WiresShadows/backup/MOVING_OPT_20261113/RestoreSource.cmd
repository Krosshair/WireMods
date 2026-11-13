@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src\"
copy /y "%~dp0README.md" "%~dp0..\..\package\README.md"
copy /y "%~dp0manifest.json" "%~dp0..\..\package\manifest.json"
echo Restored source.
pause
