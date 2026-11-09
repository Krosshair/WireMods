@echo off
rem Restores the previous ConnectionParticleSafety.cs (with ForceYellow) and ConnectionVisuals.cs (station-specific link prefab).
copy /Y "%~dp0ConnectionParticleSafety.cs" "%~dp0..\..\src\ConnectionParticleSafety.cs"
copy /Y "%~dp0ConnectionVisuals.cs" "%~dp0..\..\src\ConnectionVisuals.cs"
