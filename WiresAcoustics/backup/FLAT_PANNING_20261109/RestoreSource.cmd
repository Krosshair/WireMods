@echo off
for %%f in (Filter Directions Occlusion Travel Menu PlayerSounds) do copy /Y "%~dp0%%f.cs" "%~dp0..\..\src\%%f.cs"
