@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src\"
copy /y "%~dp0shader\*" "C:\Users\kajet\LightRework\Assets\Shaders\"
for %%f in (README.md CHANGELOG.md) do copy /y "%~dp0%%f" "%~dp0..\..\package\"
copy /y "%~dp0MOD_CONTEXT.md" "%~dp0..\..\"
echo Rebuild the bundle after restoring.
pause
