@echo off
copy /Y "%~dp0MouseControls.cs" "C:\Users\kajet\WireMods\WiresStackify\src\MouseControls.cs" || exit /b 1
echo Restored source; rebuild separately.
