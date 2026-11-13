@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src\"
copy /y "%~dp0Shaders\*" "C:\Users\kajet\LightRework\Assets\Shaders\"
copy /y "%~dp0Editor\*" "C:\Users\kajet\LightRework\Assets\Editor\"
echo Restored source, shaders and build script.
pause
