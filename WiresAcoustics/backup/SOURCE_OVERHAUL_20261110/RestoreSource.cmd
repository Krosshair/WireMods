@echo off
for %%f in (Follow.cs Equip.cs Area.cs) do copy /Y "%~dp0%%f" "%~dp0..\..\src\%%f"
move /Y "%~dp0..\..\src\Spots.cs" "%~dp0Spots.removed.cs"
