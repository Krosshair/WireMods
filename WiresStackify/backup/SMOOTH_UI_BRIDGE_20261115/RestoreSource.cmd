@echo off
set "SRC=C:\Users\kajet\WireMods\WiresStackify\src"
copy /Y "%~dp0MouseControls.cs" "%SRC%\MouseControls.cs" || exit /b 1
if exist "%SRC%\SmoothUIBridge.cs" del "%SRC%\SmoothUIBridge.cs"
echo Previous Stackify source restored; rebuild separately.
