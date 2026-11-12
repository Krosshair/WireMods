@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src_base\"
move /y "%~dp0..\..\src_base\SunCasters.cs" "%~dp0removed_SunCasters.cs" 2>nul
move /y "%~dp0..\..\src_base\LightColors.cs" "%~dp0removed_LightColors.cs" 2>nul
echo Restored source.
pause
