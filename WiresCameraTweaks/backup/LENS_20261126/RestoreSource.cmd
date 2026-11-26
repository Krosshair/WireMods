@echo off
set S=%~dp0..\..\src
if not exist "%S%\_removed" mkdir "%S%\_removed"
if exist "%S%\LensEffect.cs" move /y "%S%\LensEffect.cs" "%S%\_removed\"
copy /y "%~dp0src\*" "%S%\"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\"
copy /y "%~dp0README.md" "%~dp0..\..\package\"
echo Source restored.
pause
