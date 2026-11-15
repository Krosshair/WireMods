@echo off
set "R=C:\Users\kajet\WireMods\WiresStackify"
copy /Y "%~dp0StackifyPlugin.cs" "%R%\src\StackifyPlugin.cs" || exit /b 1
copy /Y "%~dp0README.md" "%R%\package\README.md" || exit /b 1
copy /Y "%~dp0CHANGELOG.md" "%R%\package\CHANGELOG.md" || exit /b 1
copy /Y "%~dp0manifest.json" "%R%\package\manifest.json" || exit /b 1
echo Prior documentation and version metadata restored.
