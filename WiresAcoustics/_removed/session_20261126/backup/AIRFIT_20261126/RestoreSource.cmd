@echo off
rem Acoustics source and docs back to before the air fit.
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
copy /y "%~dp0README.md" "%~dp0..\..\package\"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\"
copy /y "%~dp0MOD_CONTEXT.md" "%~dp0..\..\"
pause
