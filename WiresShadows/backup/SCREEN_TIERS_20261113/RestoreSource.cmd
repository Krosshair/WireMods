@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src\"
copy /y "%~dp0Shaders\*" "C:\Users\kajet\LightRework\Assets\Shaders\"
echo Restored source and shaders.
pause
