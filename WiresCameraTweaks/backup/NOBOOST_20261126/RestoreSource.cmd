@echo off
copy /y "%~dp0src\*" "%~dp0..\..\src\"
copy /y "%~dp0shader\WireLens.shader" "C:\Users\kajet\LightRework\Assets\Shaders\"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\"
copy /y "%~dp0README.md" "%~dp0..\..\package\"
copy /y "%~dp0MOD_CONTEXT.md" "%~dp0..\..\"
echo Source restored. Rebuild the bundle in LightRework.
pause
