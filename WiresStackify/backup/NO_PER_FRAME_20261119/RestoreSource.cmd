@echo off
copy /y "%~dp0MouseControls.cs" "%~dp0..\..\src\MouseControls.cs"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
if not exist "%~dp0..\..\_removed" mkdir "%~dp0..\..\_removed"
move /y "%~dp0..\..\src\SlotInput.cs" "%~dp0..\..\_removed\SlotInput.cs"
