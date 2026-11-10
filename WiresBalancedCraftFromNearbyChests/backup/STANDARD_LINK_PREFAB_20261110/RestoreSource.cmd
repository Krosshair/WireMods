@echo off
copy /Y "%~dp0ConnectionVisuals.cs" "%~dp0..\..\src\ConnectionVisuals.cs"
copy /Y "%~dp0Plugin.cs" "%~dp0..\..\src\Plugin.cs"
copy /Y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
