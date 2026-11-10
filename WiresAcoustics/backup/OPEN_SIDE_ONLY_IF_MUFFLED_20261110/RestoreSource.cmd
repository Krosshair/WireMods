@echo off
for %%f in (Directions.cs Area.cs) do copy /Y "%~dp0%%f" "%~dp0..\..\src\%%f"
copy /Y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
