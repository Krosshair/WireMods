@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src\"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
if not exist "%~dp0..\..\_removed" mkdir "%~dp0..\..\_removed"
move /y "%~dp0..\..\src\Interaction.cs" "%~dp0..\..\_removed\Interaction.cs"
