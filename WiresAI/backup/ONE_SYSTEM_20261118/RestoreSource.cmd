@echo off
if not exist "%~dp0..\..\_removed" mkdir "%~dp0..\..\_removed"
for %%f in ("%~dp0..\..\src\*.cs") do if not exist "%~dp0src\%%~nxf" move /y "%%f" "%~dp0..\..\_removed\%%~nxf"
copy /y "%~dp0src\*.*" "%~dp0..\..\src\"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
copy /y "%~dp0README.md" "%~dp0..\..\package\README.md"
copy /y "%~dp0NEXUS_DESCRIPTION.txt" "%~dp0..\..\NEXUS_DESCRIPTION.txt"
