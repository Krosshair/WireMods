@echo off
rem Acoustics docs back to before the 0.8.1 write-up.
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\"
copy /y "%~dp0README.md" "%~dp0..\..\package\"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\"
copy /y "%~dp0MOD_CONTEXT.md" "%~dp0..\..\"
pause
