@echo off
for %%f in (Spots.cs Follow.cs) do copy /Y "%~dp0%%f" "%~dp0..\..\src\%%f"
move /Y "%~dp0..\..\src\Animated.cs" "%~dp0Animated.removed.cs"
