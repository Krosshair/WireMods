@echo off
for %%f in (Batch.cs Area.cs Directions.cs Filter.cs) do copy /Y "%~dp0%%f" "%~dp0..\..\src\%%f"
copy /Y "%~dp0CHANGELOG.md" "%~dp0..\..\package\CHANGELOG.md"
